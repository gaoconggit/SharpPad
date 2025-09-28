using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using monacoEditorCSharp.DataHelpers;
using System.Threading;
using System.Diagnostics;
using System.IO.Compression;

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeRunner
    {
        // Manage running process contexts keyed by SessionId
        private static readonly ConcurrentDictionary<string, ProcessExecutionContext> _activeProcesses = new();

        private const string WindowsFormsHostRequirementMessage = "WinForms can only run on Windows (System.Windows.Forms/System.Drawing are Windows-only).";
        private const int DEFAULT_LANGUAGE_VERSION = 2147483647; // C# Latest
        private const int PROCESS_CLEANUP_TIMEOUT_MS = 5000;
        private const string CODE_EXECUTION_CANCELLED_MESSAGE = "Code execution was cancelled";
        private const string DOTNET_RESTORE_FAILED_MESSAGE = "dotnet restore failed with exit code";
        private const string PROCESS_EXITED_MESSAGE = "Process exited with code";
        private const string CODE_EXECUTION_STOPPED_MESSAGE = "Code execution stopped";
        private const string RESPONSE_DISPOSED_MESSAGE = "Response object disposed";
        private const string RESPONSE_WRITE_ERROR_MESSAGE = "Response write error";
        private const string CHANNEL_PROCESSING_ERROR_MESSAGE = "Error occurred while processing channel";

        private static async Task<bool> IsDotnetCliAvailableAsync()
        {
            try
            {
                var (exitCode, _, _) = await RunProcessCaptureAsync("dotnet", "--version", Environment.CurrentDirectory);
                return exitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeProjectType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return "console";
            }

            var filtered = new string(type.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(filtered))
            {
                return "console";
            }

            if (filtered.Contains("winform") || filtered.Contains("form") || filtered.Contains("windows"))
            {
                return "winforms";
            }

            if (filtered.Contains("aspnet") || filtered.Contains("webapi") || filtered == "web")
            {
                return "webapi";
            }

            return "console";
        }

        private static (OutputKind OutputKind, bool RequiresStaThread) GetRunBehavior(string projectType)
        {
            return NormalizeProjectType(projectType) switch
            {
                "winforms" => (OutputKind.WindowsApplication, true),
                _ => (OutputKind.ConsoleApplication, false)
            };
        }

        public static bool ProvideInput(string sessionId, string input)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            if (_activeProcesses.TryGetValue(sessionId, out var context))
            {
                return context.TryWriteInput(input ?? string.Empty);
            }

            return false;
        }


        public static bool TryStopProcess(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }

            if (_activeProcesses.TryGetValue(sessionId, out var context))
            {
                context.RequestStop();
                return true;
            }

            return false;
        }

        public class RunResult
        {
            public string Output { get; set; }
            public string Error { get; set; }
        }

        public static async Task<RunResult> RunMultiFileCodeAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            Func<string, Task> onOutput,
            Func<string, Task> onError,
            string sessionId = null,
            string projectType = null,
            CancellationToken cancellationToken = default)
        {
            var result = new RunResult();
            files ??= new List<FileContent>();

            var normalizedProjectType = NormalizeProjectType(projectType);
            var runBehavior = GetRunBehavior(normalizedProjectType);
            if (runBehavior.OutputKind != OutputKind.WindowsApplication && DetectWinFormsUsage(files.Select(f => f?.Content)))
            {
                normalizedProjectType = "winforms";
                runBehavior = GetRunBehavior(normalizedProjectType);
            }

            if (runBehavior.OutputKind == OutputKind.WindowsApplication && !OperatingSystem.IsWindows())
            {
                await onError(WindowsFormsHostRequirementMessage).ConfigureAwait(false);
                result.Error = WindowsFormsHostRequirementMessage;
                return result;
            }

            // Check if dotnet CLI is available
            if (!await IsDotnetCliAvailableAsync())
            {
                const string dotnetNotAvailableMessage = "dotnet CLI is not available. Please ensure .NET SDK is installed and in PATH.";
                await onError(dotnetNotAvailableMessage).ConfigureAwait(false);
                result.Error = dotnetNotAvailableMessage;
                return result;
            }

            var workingRoot = Path.Combine(Path.GetTempPath(), "SharpPadRuntime", Guid.NewGuid().ToString("N"));
            var projectDir = Path.Combine(workingRoot, "src");
            Directory.CreateDirectory(projectDir);

            var assemblyName = DeriveAssemblyNameForRun(files);
            var projectFile = Path.Combine(projectDir, $"{assemblyName}.csproj");

            ProcessExecutionContext context = null;

            try
            {
                var projectContent = BuildProjectFileContent(assemblyName, nuget, languageVersion, normalizedProjectType);
                await File.WriteAllTextAsync(projectFile, projectContent, Encoding.UTF8).ConfigureAwait(false);
                await WriteSourceFilesAsync(files, projectDir).ConfigureAwait(false);

                var restoreExitCode = await RunCommandAndStreamAsync(
                    "dotnet",
                    "restore",
                    projectDir,
                    onOutput,
                    onError,
                    cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    await onError(CODE_EXECUTION_CANCELLED_MESSAGE).ConfigureAwait(false);
                    result.Error = CODE_EXECUTION_CANCELLED_MESSAGE;
                    return result;
                }

                if (restoreExitCode != 0)
                {
                    var message = $"{DOTNET_RESTORE_FAILED_MESSAGE} {restoreExitCode}";
                    await onError(message).ConfigureAwait(false);
                    result.Error = message;
                    return result;
                }

                var runArgs = $"run --no-restore --project \"{projectFile}\"";
                context = StartStreamingProcess(
                    "dotnet",
                    runArgs,
                    projectDir,
                    workingRoot,
                    redirectStandardInput: true,
                    onOutput,
                    onError);

                if (!string.IsNullOrEmpty(sessionId))
                {
                    _activeProcesses.AddOrUpdate(sessionId, context, (_, existing) =>
                    {
                        existing?.RequestStop();
                        return context;
                    });
                }

                using var cancellationRegistration = cancellationToken.Register(() => context.RequestStop());

                var exitCode = await context.WaitForExitAsync().ConfigureAwait(false);

                if (context.IsStopRequested || cancellationToken.IsCancellationRequested)
                {
                    await onError(CODE_EXECUTION_CANCELLED_MESSAGE).ConfigureAwait(false);
                    result.Error = CODE_EXECUTION_CANCELLED_MESSAGE;
                }
                else if (exitCode != 0)
                {
                    var message = $"{PROCESS_EXITED_MESSAGE} {exitCode}";
                    await onError(message).ConfigureAwait(false);
                    result.Error = message;
                }

                result.Output = string.Empty;
                result.Error ??= string.Empty;
                return result;
            }
            catch (Exception ex)
            {
                await onError("Runtime error: " + ex.Message).ConfigureAwait(false);
                result.Output = string.Empty;
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                if (!string.IsNullOrEmpty(sessionId))
                {
                    if (_activeProcesses.TryGetValue(sessionId, out var existing) && ReferenceEquals(existing, context))
                    {
                        _activeProcesses.TryRemove(sessionId, out _);
                    }
                }

                if (context != null)
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    CleanupWorkingDirectory(workingRoot);
                }
            }
        }

        public static Task<RunResult> RunProgramCodeAsync(
            string code,
            string nuget,
            int languageVersion,
            Func<string, Task> onOutput,
            Func<string, Task> onError,
            string sessionId = null,
            string projectType = null,
            CancellationToken cancellationToken = default)
        {
            var files = new List<FileContent>
            {
                new FileContent { FileName = "Program.cs", Content = code ?? string.Empty }
            };

            return RunMultiFileCodeAsync(
                files,
                nuget,
                languageVersion,
                onOutput,
                onError,
                sessionId,
                projectType,
                cancellationToken);
        }




        private static bool DetectWinFormsUsage(IEnumerable<string> sources)
        {
            if (sources == null)
            {
                return false;
            }

            foreach (var source in sources)
            {
                if (DetectWinFormsUsage(source))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DetectWinFormsUsage(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            return source.IndexOf("System.Windows.Forms", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("Application.Run", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf(": Form", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf(" new Form", StringComparison.OrdinalIgnoreCase) >= 0;
        }




        private static async Task<int> RunCommandAndStreamAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            Func<string, Task> onOutput,
            Func<string, Task> onError,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutPump = PumpStreamAsync(process.StandardOutput, onOutput);
            var stderrPump = PumpStreamAsync(process.StandardError, onError);

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                    // ignore termination failures
                }
            });

            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdoutPump, stderrPump).ConfigureAwait(false);
            return process.ExitCode;
        }

        private static ProcessExecutionContext StartStreamingProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            string cleanupRoot,
            bool redirectStandardInput,
            Func<string, Task> onOutput,
            Func<string, Task> onError)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = redirectStandardInput,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutPump = PumpStreamAsync(process.StandardOutput, onOutput);
            var stderrPump = PumpStreamAsync(process.StandardError, onError);

            return new ProcessExecutionContext(process, cleanupRoot, stdoutPump, stderrPump);
        }

        private static async Task PumpStreamAsync(StreamReader reader, Func<string, Task> callback)
        {
            try
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }

                    if (callback != null)
                    {
                        await callback(line + Environment.NewLine).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // ignore pump failures (process likely terminated)
            }
        }

        private static void CleanupWorkingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // ignore cleanup failures
            }
        }

        private sealed class ProcessExecutionContext : IAsyncDisposable
        {
            private readonly Task _stdoutPump;
            private readonly Task _stderrPump;
            private readonly object _writerLock = new();
            private int _stopRequested;

            public ProcessExecutionContext(Process process, string cleanupRoot, Task stdoutPump, Task stderrPump)
            {
                Process = process ?? throw new ArgumentNullException(nameof(process));
                WorkingDirectory = cleanupRoot;
                _stdoutPump = stdoutPump ?? Task.CompletedTask;
                _stderrPump = stderrPump ?? Task.CompletedTask;
                if (process.StartInfo.RedirectStandardInput)
                {
                    StandardInput = process.StandardInput;
                    StandardInput.AutoFlush = true;
                }
            }

            public Process Process { get; }
            public string WorkingDirectory { get; }
            public StreamWriter StandardInput { get; }
            public bool IsStopRequested => Volatile.Read(ref _stopRequested) == 1;

            public bool TryWriteInput(string input)
            {
                if (StandardInput == null)
                {
                    return false;
                }

                lock (_writerLock)
                {
                    if (Process.HasExited)
                    {
                        return false;
                    }

                    try
                    {
                        StandardInput.WriteLine(input);
                        StandardInput.Flush();
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            public void RequestStop()
            {
                if (Interlocked.Exchange(ref _stopRequested, 1) == 1)
                {
                    return;
                }

                try
                {
                    if (!Process.HasExited)
                    {
                        Process.Kill(true);

                        // Wait for graceful termination with timeout
                        if (!Process.WaitForExit(PROCESS_CLEANUP_TIMEOUT_MS))
                        {
                            // Force termination if graceful exit timeout
                            try
                            {
                                Process.Kill(true);
                            }
                            catch
                            {
                                // ignore force termination failures
                            }
                        }
                    }
                }
                catch
                {
                    // ignore termination failures
                }

                try
                {
                    StandardInput?.Close();
                }
                catch
                {
                    // ignore input disposal issues
                }
            }

            public async Task<int> WaitForExitAsync()
            {
                await Process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(_stdoutPump, _stderrPump).ConfigureAwait(false);
                return Process.ExitCode;
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Task.WhenAll(_stdoutPump, _stderrPump).ConfigureAwait(false);
                }
                catch
                {
                    // ignore pump failures during dispose
                }

                Process.Dispose();
                CleanupWorkingDirectory(WorkingDirectory);
            }
        }

        private static List<string> BuildPackageReferenceLines(string nuget)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(nuget))
            {
                return lines;
            }

            foreach (var part in nuget.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var items = part.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var id = items.Length > 0 ? items[0].Trim() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var version = items.Length > 1 ? items[1].Trim() : null;
                if (string.IsNullOrWhiteSpace(version))
                {
                    lines.Add("    <PackageReference Include=\"" + id + "\" />");
                }
                else
                {
                    lines.Add("    <PackageReference Include=\"" + id + "\" Version=\"" + version + "\" />");
                }
            }

            return lines;
        }
        private static string GetHostTargetFramework(bool requireWindows)
        {
            var version = Environment.Version;
            var major = version.Major >= 5 ? version.Major : 6;
            var minor = version.Major >= 5 ? Math.Max(0, version.Minor) : 0;
            var moniker = $"net{major}.{minor}";
            if (requireWindows)
            {
                moniker += "-windows";
            }
            return moniker;
        }

        private static IEnumerable<string> GetWindowsDesktopReferencePaths(IEnumerable<string> assemblyNames)
        {
            if (assemblyNames == null)
            {
                return Array.Empty<string>();
            }

            var names = assemblyNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (names.Length == 0)
            {
                return Array.Empty<string>();
            }

            foreach (var packDir in EnumerateWindowsDesktopPackDirectories())
            {
                var refRoot = Path.Combine(packDir, "ref");
                if (!Directory.Exists(refRoot))
                {
                    continue;
                }

                foreach (var tfmDir in EnumerateTfmDirectories(refRoot))
                {
                    var resolved = new List<string>();
                    foreach (var name in names)
                    {
                        var candidate = Path.Combine(tfmDir, name + ".dll");
                        if (File.Exists(candidate))
                        {
                            resolved.Add(candidate);
                        }
                    }

                    if (resolved.Count > 0)
                    {
                        return resolved;
                    }
                }
            }

            return Array.Empty<string>();
        }

        private static IEnumerable<string> EnumerateWindowsDesktopPackDirectories()
        {
            foreach (var root in EnumerateDotnetRootCandidates())
            {
                var packBase = Path.Combine(root, "packs", "Microsoft.WindowsDesktop.App.Ref");
                if (!Directory.Exists(packBase))
                {
                    continue;
                }

                var versionDirs = Directory.GetDirectories(packBase)
                    .Select(dir => new { dir, version = TryParseVersionFromDirectory(Path.GetFileName(dir)) })
                    .OrderByDescending(item => item.version)
                    .ThenByDescending(item => item.dir, StringComparer.OrdinalIgnoreCase);

                foreach (var item in versionDirs)
                {
                    yield return item.dir;
                }
            }
        }

        private static IEnumerable<string> EnumerateDotnetRootCandidates()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddIfExists(string pathCandidate)
            {
                if (string.IsNullOrWhiteSpace(pathCandidate))
                {
                    return;
                }

                try
                {
                    var fullPath = Path.GetFullPath(pathCandidate.Trim());
                    if (Directory.Exists(fullPath))
                    {
                        roots.Add(fullPath);
                    }
                }
                catch
                {
                    // ignore invalid paths
                }
            }

            AddIfExists(Environment.GetEnvironmentVariable("DOTNET_ROOT"));
            AddIfExists(Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"));

            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    var dir = Path.GetDirectoryName(processPath);
                    AddIfExists(dir);
                }
            }
            catch
            {
                // ignore access errors
            }

            try
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(programFiles))
                {
                    AddIfExists(Path.Combine(programFiles, "dotnet"));
                }
            }
            catch
            {
                // ignore folder resolution errors
            }

            try
            {
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(programFilesX86))
                {
                    AddIfExists(Path.Combine(programFilesX86, "dotnet"));
                }
            }
            catch
            {
                // ignore folder resolution errors
            }

            return roots;
        }

        private static IEnumerable<string> EnumerateTfmDirectories(string refRoot)
        {
            if (!Directory.Exists(refRoot))
            {
                yield break;
            }

            var runtimeVersion = Environment.Version;

            var candidates = Directory.GetDirectories(refRoot)
                .Select(dir =>
                {
                    var name = Path.GetFileName(dir);
                    var version = ParseTfmVersion(name);
                    var isWindows = name.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0;
                    var scoreMajor = version.Major == runtimeVersion.Major ? 1 : 0;
                    var scoreMinor = version.Minor == runtimeVersion.Minor ? 1 : 0;
                    return new
                    {
                        Dir = dir,
                        Name = name,
                        Version = version,
                        IsWindows = isWindows,
                        ScoreMajor = scoreMajor,
                        ScoreMinor = scoreMinor
                    };
                })
                .OrderByDescending(c => c.IsWindows)
                .ThenByDescending(c => c.ScoreMajor)
                .ThenByDescending(c => c.ScoreMinor)
                .ThenByDescending(c => c.Version)
                .ThenByDescending(c => c.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                yield return candidate.Dir;
            }
        }

        private static Version ParseTfmVersion(string tfm)
        {
            if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            {
                return new Version(0, 0);
            }

            var span = tfm.AsSpan(3);
            var dashIndex = span.IndexOf('-');
            if (dashIndex >= 0)
            {
                span = span[..dashIndex];
            }

            return Version.TryParse(span.ToString(), out var version) ? version : new Version(0, 0);
        }

        private static Version TryParseVersionFromDirectory(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new Version(0, 0);
            }

            return Version.TryParse(name, out var version) ? version : new Version(0, 0);
        }

        private static string BuildProjectFileContent(string assemblyName, string nuget, int languageVersion, string projectType)
        {
            var normalizedType = NormalizeProjectType(projectType);
            var sdk = "Microsoft.NET.Sdk";
            var targetFramework = GetHostTargetFramework(requireWindows: false);
            var outputTypeValue = "Exe";
            var propertyExtraLines = new List<string>();
            var frameworkReferences = new List<string>();

            switch (normalizedType)
            {
                case "winforms":
                    targetFramework = GetHostTargetFramework(requireWindows: true);
                    outputTypeValue = "WinExe";
                    propertyExtraLines.Add("    <UseWindowsForms>true</UseWindowsForms>");
                    propertyExtraLines.Add("    <EnableWindowsTargeting>true</EnableWindowsTargeting>");
                    frameworkReferences.Add("    <FrameworkReference Include=\"Microsoft.WindowsDesktop.App\" />");
                    break;
                case "webapi":
                    sdk = "Microsoft.NET.Sdk.Web";
                    break;
            }

            var langVer = "latest";
            try
            {
                langVer = ((LanguageVersion)languageVersion).ToString();
            }
            catch
            {
                // keep default when conversion fails
            }

            var builder = new StringBuilder();
            builder.AppendLine($"<Project Sdk=\"{sdk}\">");
            builder.AppendLine("  <PropertyGroup>");
            builder.AppendLine($"    <OutputType>{outputTypeValue}</OutputType>");
            builder.AppendLine($"    <TargetFramework>{targetFramework}</TargetFramework>");
            builder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
            builder.AppendLine("    <Nullable>enable</Nullable>");
            builder.AppendLine($"    <AssemblyName>{assemblyName}</AssemblyName>");
            builder.AppendLine($"    <RootNamespace>{assemblyName}</RootNamespace>");
            builder.AppendLine($"    <LangVersion>{langVer}</LangVersion>");
            foreach (var line in propertyExtraLines)
            {
                builder.AppendLine(line);
            }
            builder.AppendLine("  </PropertyGroup>");

            var packages = BuildPackageReferenceLines(nuget);
            if (packages.Count > 0)
            {
                builder.AppendLine("  <ItemGroup>");
                foreach (var line in packages)
                {
                    builder.AppendLine(line);
                }
                builder.AppendLine("  </ItemGroup>");
            }

            if (frameworkReferences.Count > 0)
            {
                builder.AppendLine("  <ItemGroup>");
                foreach (var line in frameworkReferences)
                {
                    builder.AppendLine(line);
                }
                builder.AppendLine("  </ItemGroup>");
            }

            builder.AppendLine("</Project>");
            return builder.ToString();
        }

        private static string DeriveAssemblyNameForRun(List<FileContent> files)
        {
            var candidate = files?.FirstOrDefault(f => f?.IsEntry == true)?.FileName
                ?? files?.FirstOrDefault()?.FileName
                ?? "Program.cs";

            var baseName = Path.GetFileNameWithoutExtension(candidate);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "SharpPadProgram";
            }

            var sanitized = new string(baseName.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "SharpPadProgram" : sanitized;
        }

        private static async Task WriteSourceFilesAsync(List<FileContent> files, string destinationDir)
        {
            if (files == null)
            {
                return;
            }

            foreach (var file in files)
            {
                var relative = string.IsNullOrWhiteSpace(file?.FileName) ? "Program.cs" : file.FileName;
                relative = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    segments = new[] { "Program.cs" };
                }

                for (var i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    foreach (var invalid in Path.GetInvalidFileNameChars())
                    {
                        segment = segment.Replace(invalid, '_');
                    }
                    segments[i] = segment;
                }

                var safeRelativePath = Path.Combine(segments);
                var destinationPath = Path.Combine(destinationDir, safeRelativePath);

                // Security check: Prevent path traversal attacks
                var fullDestinationPath = Path.GetFullPath(destinationPath);
                var fullDestinationDir = Path.GetFullPath(destinationDir);
                if (!fullDestinationPath.StartsWith(fullDestinationDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !fullDestinationPath.Equals(fullDestinationDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Path traversal attempt detected: {safeRelativePath}");
                }

                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(destinationPath, file?.Content ?? string.Empty, Encoding.UTF8).ConfigureAwait(false);
            }
        }

        private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessCaptureAsync(string fileName, string arguments, string workingDirectory, IDictionary<string, string> environment = null)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (environment != null)
            {
                foreach (var kv in environment)
                {
                    psi.Environment[kv.Key] = kv.Value;
                }
            }

            using var process = new Process { StartInfo = psi };
            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync().ConfigureAwait(false);

            return (process.ExitCode, stdOut.ToString(), stdErr.ToString());
        }

        public static void DownloadPackage(string nuget)
        {
            DownloadNugetPackages.DownloadAllPackages(nuget);
        }

        public static async Task<ExeBuildResult> BuildMultiFileExecutableAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            string outputFileName,
            string projectType)
        {
            return await BuildWithDotnetPublishAsync(files, nuget, languageVersion, outputFileName, projectType);
        }

        public static async Task<ExeBuildResult> BuildExecutableAsync(
            string code,
            string nuget,
            int languageVersion,
            string outputFileName,
            string projectType)
        {
            var files = new List<FileContent> { new FileContent { FileName = "Program.cs", Content = code } };
            return await BuildWithDotnetPublishAsync(files, nuget, languageVersion, outputFileName, projectType);
        }

        private static async Task<ExeBuildResult> BuildWithDotnetPublishAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            string outputFileName,
            string projectType)
        {
            var result = new ExeBuildResult();

            try
            {
                var workingRoot = Path.Combine(Path.GetTempPath(), "SharpPadBuilds", Guid.NewGuid().ToString("N"));
                var srcDir = Path.Combine(workingRoot, "src");
                var publishDir = Path.Combine(workingRoot, "publish");
                Directory.CreateDirectory(srcDir);
                Directory.CreateDirectory(publishDir);

                var outName = string.IsNullOrWhiteSpace(outputFileName) ? "Program.exe" : outputFileName;
                var asmName = Path.GetFileNameWithoutExtension(outName);
                var artifactFileName = Path.ChangeExtension(outName, ".zip");

                var csprojPath = Path.Combine(srcDir, $"{asmName}.csproj");

                // Use the shared project file generation logic
                var projectContent = BuildProjectFileContent(asmName, nuget, languageVersion, projectType);
                await File.WriteAllTextAsync(csprojPath, projectContent, Encoding.UTF8);

                foreach (var f in files)
                {
                    var safeName = string.IsNullOrWhiteSpace(f?.FileName) ? "Program.cs" : f.FileName;
                    foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
                    var dest = Path.Combine(srcDir, safeName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await File.WriteAllTextAsync(dest, f?.Content ?? string.Empty, Encoding.UTF8);
                }

                static async Task<(int code, string stdout, string stderr)> RunAsync(string fileName, string args, string workingDir)
                {
                    var psi = new ProcessStartInfo(fileName, args)
                    {
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    var p = new Process { StartInfo = psi };
                    var sbOut = new StringBuilder();
                    var sbErr = new StringBuilder();
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    await p.WaitForExitAsync();
                    return (p.ExitCode, sbOut.ToString(), sbErr.ToString());
                }

                var (rc1, o1, e1) = await RunAsync("dotnet", "restore", srcDir);
                if (rc1 != 0)
                {
                    var msg = $"dotnet restore failed.\n{o1}\n{e1}";
                    result.Success = false;
                    result.Error = msg;
                    return result;
                }

                var rid = GetRuntimeIdentifier();
                var publishArgs = $"publish -c Release -r {rid} --self-contained true -o \"{publishDir}\"";
                var (rc2, o2, e2) = await RunAsync("dotnet", publishArgs, srcDir);
                if (rc2 != 0)
                {
                    var msg = $"dotnet publish failed.\n{o2}\n{e2}";
                    result.Success = false;
                    result.Error = msg;
                    return result;
                }

                // Find the actual executable file
                string exePath;
                if (OperatingSystem.IsWindows())
                {
                    var defaultExe = Path.Combine(publishDir, asmName + ".exe");
                    var requestedExe = Path.Combine(publishDir, outName);
                    if (File.Exists(defaultExe) && !defaultExe.Equals(requestedExe, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(requestedExe)) File.Delete(requestedExe);
                        File.Move(defaultExe, requestedExe);
                        exePath = requestedExe;
                    }
                    else
                    {
                        exePath = File.Exists(requestedExe) ? requestedExe : defaultExe;
                    }
                }
                else
                {
                    // On Linux/macOS, executable doesn't have .exe extension
                    exePath = Path.Combine(publishDir, asmName);
                    if (!File.Exists(exePath))
                    {
                        // Look for any executable file in the publish directory
                        var executableFiles = Directory.GetFiles(publishDir).Where(f =>
                        {
                            var info = new FileInfo(f);
                            return info.Name == asmName || info.Name.StartsWith(asmName);
                        });
                        exePath = executableFiles.FirstOrDefault() ?? exePath;
                    }
                }

                if (!File.Exists(exePath))
                {
                    result.Success = false;
                    result.Error = $"Executable file not found at: {exePath}";
                    return result;
                }

                var artifactPath = Path.Combine(workingRoot, artifactFileName);
                if (File.Exists(artifactPath))
                {
                    File.Delete(artifactPath);
                }

                ZipFile.CreateFromDirectory(publishDir, artifactPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                result.Success = true;
                result.ExeFilePath = artifactPath;
                result.FileSizeBytes = new FileInfo(artifactPath).Length;
                result.CompilationMessages.Add($"Built package: {Path.GetFileName(artifactPath)}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Build error: {ex.Message}";
            }

            return result;
        }

        private static string GetRuntimeIdentifier()
        {
            if (OperatingSystem.IsWindows())
                return "win-x64";
            if (OperatingSystem.IsMacOS())
                return "osx-x64";
            return "linux-x64";
        }

    }
}












