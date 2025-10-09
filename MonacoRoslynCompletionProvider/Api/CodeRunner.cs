using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using monacoEditorCSharp.DataHelpers;
using System.Threading;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Loader;
using MonacoRoslynCompletionProvider;

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeRunner
    {
        private static readonly Dictionary<string, ProcessSession> ActiveProcessSessions = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, InteractiveTextReader> LegacyReaders = new();
        private static readonly object ProcessSessionLock = new();
        private static readonly object ConsoleLock = new();

        private const string WindowsFormsHostRequirementMessage = "WinForms can only run on Windows (System.Windows.Forms/System.Drawing are Windows-only).";

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

        private static Task RunEntryPointAsync(Func<Task> executeAsync, bool requiresStaThread)
        {
            if (executeAsync == null) throw new ArgumentNullException(nameof(executeAsync));

            if (!requiresStaThread)
            {
                return executeAsync();
            }

            if (!OperatingSystem.IsWindows())
            {
                return executeAsync();
            }

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    executeAsync().GetAwaiter().GetResult();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "CodeRunner-STA"
            };

            try
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            catch (PlatformNotSupportedException)
            {
                return executeAsync();
            }

            thread.Start();
            return tcs.Task;
        }

        public static bool ProvideInput(string sessionId, string input)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }

            lock (LegacyReaders)
            {
                if (LegacyReaders.TryGetValue(sessionId, out var reader))
                {
                    reader.ProvideInput(input);
                    return true;
                }
            }

            lock (ProcessSessionLock)
            {
                if (ActiveProcessSessions.TryGetValue(sessionId, out var session))
                {
                    return session.ProvideInput(input);
                }
            }

            return false;
        }

        public class RunResult
        {
            public string Output { get; set; }
            public string Error { get; set; }
        }


        public static Task<RunResult> RunMultiFileCodeAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            Func<string, Task> onOutput,
            Func<string, Task> onError,
            string sessionId = null,
            string projectType = null,
            CancellationToken cancellationToken = default)
        {
            return CompileAndExecuteAsync(
                files ?? new List<FileContent>(),
                nuget,
                languageVersion,
                onOutput,
                onError,
                sessionId,
                projectType,
                cancellationToken);
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
                new FileContent
                {
                    FileName = "Program.cs",
                    Content = code ?? string.Empty
                }
            };

            return CompileAndExecuteAsync(
                files,
                nuget,
                languageVersion,
                onOutput,
                onError,
                sessionId,
                projectType,
                cancellationToken);
        }

        private static async Task<RunResult> CompileAndExecuteAsync(
            IEnumerable<FileContent> sourceFiles,
            string nuget,
            int languageVersion,
            Func<string, Task> onOutput,
            Func<string, Task> onError,
            string sessionId,
            string projectType,
            CancellationToken cancellationToken)
        {
            var result = new RunResult
            {
                Output = string.Empty,
                Error = string.Empty
            };

            if (onOutput is null)
            {
                throw new ArgumentNullException(nameof(onOutput));
            }

            if (onError is null)
            {
                throw new ArgumentNullException(nameof(onError));
            }

            var files = (sourceFiles ?? Enumerable.Empty<FileContent>())
                .Where(f => f != null)
                .Select(f => new FileContent
                {
                    FileName = string.IsNullOrWhiteSpace(f.FileName) ? "Program.cs" : f.FileName,
                    Content = f.Content ?? string.Empty
                })
                .ToList();

            if (files.Count == 0)
            {
                files.Add(new FileContent
                {
                    FileName = "Program.cs",
                    Content = string.Empty
                });
            }

            var normalizedProjectType = NormalizeProjectType(projectType);
            var runBehavior = GetRunBehavior(projectType);
            
            // 检测是否需要 WinForms 支持
            if (runBehavior.OutputKind != OutputKind.WindowsApplication && DetectWinFormsUsage(files?.Select(f => f?.Content)))
            {
                // 自动检测到 WinForms 代码时启用所需的运行时设置
                runBehavior = (OutputKind.WindowsApplication, true);
            }

            if (runBehavior.OutputKind == OutputKind.WindowsApplication && !OperatingSystem.IsWindows())
            {
                await onError(WindowsFormsHostRequirementMessage).ConfigureAwait(false);
                result.Error = WindowsFormsHostRequirementMessage;
                return result;
            }

            // 根据项目类型选择执行方式
            if (runBehavior.OutputKind == OutputKind.WindowsApplication || 
                normalizedProjectType == "webapi" || 
                normalizedProjectType == "aspnetcore" || 
                normalizedProjectType == "aspnetcorewebapi" || 
                normalizedProjectType == "web")
            {
                // WinForms 和 Web API 应用使用进程外执行
                return await ExecuteInIsolatedProcessAsync(files, nuget, languageVersion, onOutput, onError, sessionId, projectType, cancellationToken);
            }
            else
            {
                // Console 应用使用进程内执行
                return await ExecuteInProcessAsync(files, nuget, languageVersion, onOutput, onError, sessionId, projectType, cancellationToken);
            }
        }

        // Console 应用进程内执行
        private static async Task<RunResult> ExecuteInProcessAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            Func<string, Task> onOutput,
            Func<string, Task> onError,
            string sessionId,
            string projectType,
            CancellationToken cancellationToken)
        {
            var result = new RunResult
            {
                Output = string.Empty,
                Error = string.Empty
            };

            CustomAssemblyLoadContext loadContext = null;
            Assembly assembly = null;

            try
            {
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);
                loadContext = new CustomAssemblyLoadContext(nugetAssemblies);

                var parseOptions = new CSharpParseOptions(
                    languageVersion: (LanguageVersion)languageVersion,
                    kind: SourceCodeKind.Regular,
                    documentationMode: DocumentationMode.Parse);

                string assemblyName = "DynamicCode";

                // Parse all files into syntax trees
                var syntaxTrees = new List<SyntaxTree>();
                foreach (var file in files)
                {
                    var syntaxTree = CSharpSyntaxTree.ParseText(
                        file.Content,
                        parseOptions,
                        path: file.FileName);
                    syntaxTrees.Add(syntaxTree);
                }

                // Collect references
                var references = new List<MetadataReference>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                        references.Add(MetadataReference.CreateFromFile(asm.Location));
                }

                foreach (var pkg in nugetAssemblies)
                {
                    references.Add(MetadataReference.CreateFromFile(pkg.Path));
                }

                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(OutputKind.ConsoleApplication));

                using (var peStream = new MemoryStream())
                {
                    var compileResult = compilation.Emit(peStream);
                    if (!compileResult.Success)
                    {
                        foreach (var diag in compileResult.Diagnostics)
                        {
                            if (diag.Severity == DiagnosticSeverity.Error)
                            {
                                var location = diag.Location.GetLineSpan();
                                var fileName = location.Path ?? "unknown";
                                var line = location.StartLinePosition.Line + 1;
                                await onError($"[{fileName}:{line}] {diag.GetMessage()}").ConfigureAwait(false);
                            }
                        }
                        result.Error = "Compilation error";
                        return result;
                    }
                    peStream.Seek(0, SeekOrigin.Begin);

                    assembly = loadContext.LoadFromStream(peStream);

                    var entryPoint = assembly.EntryPoint;
                    if (entryPoint != null)
                    {
                        var parameters = entryPoint.GetParameters();

                        async Task WriteAction(string text) => await onOutput(text ?? string.Empty).ConfigureAwait(false);
                        await using var outputWriter = new ImmediateCallbackTextWriter(WriteAction);

                        async Task ErrorAction(string text) => await onError(text ?? string.Empty).ConfigureAwait(false);
                        await using var errorWriter = new ImmediateCallbackTextWriter(ErrorAction);

                        var interactiveReader = new InteractiveTextReader(async prompt =>
                        {
                            await onOutput($"[INPUT REQUIRED] Please provide input: ").ConfigureAwait(false);
                        });

                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            lock (LegacyReaders)
                            {
                                LegacyReaders[sessionId] = interactiveReader;
                            }
                        }

                        TextWriter originalOut = null, originalError = null;
                        TextReader originalIn = null;
                        lock (ConsoleLock)
                        {
                            originalOut = Console.Out;
                            originalError = Console.Error;
                            originalIn = Console.In;
                            Console.SetOut(outputWriter);
                            Console.SetError(errorWriter);
                            Console.SetIn(interactiveReader);
                        }

                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
                            {
                                entryPoint.Invoke(null, new object[] { new string[] { "sharpPad" } });
                            }
                            else
                            {
                                entryPoint.Invoke(null, null);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            await onError("代码执行已被取消").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            var errorMessage = "Execution error: " + (ex.InnerException?.Message ?? ex.Message);
                            await onError(errorMessage).ConfigureAwait(false);
                        }
                        finally
                        {
                            lock (ConsoleLock)
                            {
                                Console.SetOut(originalOut);
                                Console.SetError(originalError);
                                Console.SetIn(originalIn);
                            }

                            if (!string.IsNullOrEmpty(sessionId))
                            {
                                lock (LegacyReaders)
                                {
                                    LegacyReaders.Remove(sessionId);
                                }
                            }
                            interactiveReader?.Dispose();
                        }
                    }
                    else
                    {
                        await onError("No entry point found in the code. Please ensure one file contains a Main method.").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                await onError("Runtime error: " + ex.Message).ConfigureAwait(false);
            }
            finally
            {
                assembly = null;
                loadContext?.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return result;
        }

        // WinForms 和 Web API 应用进程外执行
        private static async Task<RunResult> ExecuteInIsolatedProcessAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            Func<string, Task> onOutput,
            Func<string, Task> onError,
            string sessionId,
            string projectType,
            CancellationToken cancellationToken)
        {
            var result = new RunResult
            {
                Output = string.Empty,
                Error = string.Empty
            };

            var normalizedProjectType = NormalizeProjectType(projectType);
            var runBehavior = GetRunBehavior(projectType);

            // 检测是否需要 WinForms 支持
            if (runBehavior.OutputKind != OutputKind.WindowsApplication && DetectWinFormsUsage(files?.Select(f => f?.Content)))
            {
                runBehavior = (OutputKind.WindowsApplication, true);
            }

            var parseOptions = new CSharpParseOptions(
                languageVersion: (LanguageVersion)languageVersion,
                kind: SourceCodeKind.Regular,
                documentationMode: DocumentationMode.Parse);

            var syntaxTrees = new List<SyntaxTree>(files.Count);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceText = SourceText.From(file.Content ?? string.Empty, Encoding.UTF8);
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, path: file.FileName);
                syntaxTrees.Add(syntaxTree);
            }

            var references = new List<MetadataReference>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(asm.Location));
                }
            }

            if (runBehavior.OutputKind == OutputKind.WindowsApplication)
            {
                EnsureWinFormsAssembliesLoaded();
                TryAddWinFormsReferences(references);
            }

            var packageReferenceMap = BuildPackageReferenceMap(nuget, normalizedProjectType);
            var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);
            foreach (var package in nugetAssemblies)
            {
                references.Add(MetadataReference.CreateFromFile(package.Path));
            }

            foreach (var pkg in packageReferenceMap.Keys)
            {
                TryEnsureAssemblyLoaded(pkg);
            }

            var compilation = CSharpCompilation.Create(
                "DynamicProgram",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(runBehavior.OutputKind));

            var workingDirectory = CreateWorkingDirectory();
            var assemblyPath = Path.Combine(workingDirectory, "DynamicProgram.dll");
            var pdbPath = Path.Combine(workingDirectory, "DynamicProgram.pdb");

            try
            {
                using (var peStream = File.Create(assemblyPath))
                using (var pdbStream = File.Create(pdbPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var emitResult = compilation.Emit(
                        peStream,
                        pdbStream,
                        options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

                    if (!emitResult.Success)
                    {
                        foreach (var diagnostic in emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                        {
                            await onError(diagnostic.ToString()).ConfigureAwait(false);
                        }

                        result.Error = "Compilation error";
                        return result;
                    }
                }

                var copiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var package in nugetAssemblies)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var destination = Path.Combine(workingDirectory, Path.GetFileName(package.Path));
                    if (string.Equals(package.Path, destination, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!copiedFiles.Add(destination))
                    {
                        continue;
                    }

                    try
                    {
                        File.Copy(package.Path, destination, overwrite: true);
                    }
                    catch
                    {
                        // ignore copy failures; the execution host may still resolve assemblies from their original locations
                    }
                }

                CopyHostAssembliesForExecution(workingDirectory, copiedFiles);
                CopyAssembliesForPackages(packageReferenceMap.Keys, workingDirectory, copiedFiles);

                var hostAssemblyPath = LocateExecutionHost();
                cancellationToken.ThrowIfCancellationRequested();

                await ExecuteInIsolatedProcessCoreAsync(
                    hostAssemblyPath,
                    workingDirectory,
                    assemblyPath,
                    runBehavior.RequiresStaThread,
                    onOutput,
                    onError,
                    sessionId,
                    cancellationToken).ConfigureAwait(false);

                result.Output = string.Empty;
                result.Error = string.Empty;
                return result;
            }
            catch (OperationCanceledException)
            {
                await onError("代码执行已被取消").ConfigureAwait(false);
                result.Error = "代码执行已被取消";
                return result;
            }
            catch (Exception ex)
            {
                await onError("Runtime error: " + ex.Message).ConfigureAwait(false);
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                TryDeleteDirectory(workingDirectory);
            }
        }

        // 自定义可卸载的AssemblyLoadContext
        private static string CreateWorkingDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "SharpPad", "sessions");
            Directory.CreateDirectory(root);

            var directory = Path.Combine(root, $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static async Task<int> ExecuteInIsolatedProcessCoreAsync(
            string hostAssemblyPath,
            string workingDirectory,
            string compiledAssemblyPath,
            bool requiresStaThread,
            Func<string, Task> onOutput,
            Func<string, Task> onError,
            string sessionId,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add(hostAssemblyPath);
            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(compiledAssemblyPath);
            startInfo.ArgumentList.Add("--workingDirectory");
            startInfo.ArgumentList.Add(workingDirectory);
            startInfo.ArgumentList.Add("--requiresSta");
            startInfo.ArgumentList.Add(requiresStaThread ? "true" : "false");

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start execution host process.");
            }

            if (process.StandardInput == null)
            {
                throw new InvalidOperationException("Execution host stdin was not redirected.");
            }

            ProcessSession session = null;
            if (!string.IsNullOrEmpty(sessionId))
            {
                session = new ProcessSession(process);
                lock (ProcessSessionLock)
                {
                    ActiveProcessSessions[sessionId] = session;
                }
            }

            var cancellationRequested = false;
            using var registration = cancellationToken.Register(() =>
            {
                cancellationRequested = true;
                TryTerminateProcess(process);
            });

            var stdoutTask = PumpStreamAsync(process.StandardOutput, onOutput, cancellationToken);
            var stderrTask = PumpStreamAsync(process.StandardError, onError, cancellationToken);

            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            }
            finally
            {
                if (!string.IsNullOrEmpty(sessionId))
                {
                    lock (ProcessSessionLock)
                    {
                        ActiveProcessSessions.Remove(sessionId);
                    }
                }
            }

            if (cancellationRequested || cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return process.ExitCode;
        }

        private static async Task PumpStreamAsync(StreamReader reader, Func<string, Task> callback, CancellationToken cancellationToken)
        {
            if (reader == null)
            {
                return;
            }

            var buffer = new char[1024];

            try
            {
                while (true)
                {
                    var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    var text = new string(buffer, 0, read);
                    await callback(text).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
        }

        private static string LocateExecutionHost()
        {
            var baseDirectory = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDirectory, "ExecutionHost", "SharpPad.ExecutionHost.dll"),
                Path.Combine(baseDirectory, "SharpPad.ExecutionHost.dll"),
                Path.Combine(baseDirectory, "..", "ExecutionHost", "SharpPad.ExecutionHost.dll")
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException("Unable to locate SharpPad.ExecutionHost.dll. Ensure the ExecutionHost project is built and copied to the output directory.");
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        private static void TryTerminateProcess(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore termination failures
            }
        }

        private sealed class ProcessSession
        {
            private readonly Process _process;
            private readonly StreamWriter _inputWriter;

            public ProcessSession(Process process)
            {
                _process = process ?? throw new ArgumentNullException(nameof(process));
                _inputWriter = process.StandardInput ?? throw new InvalidOperationException("Process stdin not available.");
                _inputWriter.AutoFlush = true;
            }

            public bool ProvideInput(string input)
            {
                if (_process.HasExited)
                {
                    return false;
                }

                lock (_inputWriter)
                {
                    try
                    {
                        _inputWriter.WriteLine(input ?? string.Empty);
                        _inputWriter.Flush(); // 确保数据立即发送到子进程
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
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

        private static void EnsureWinFormsAssembliesLoaded()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            TryLoadType("System.Windows.Forms.Form, System.Windows.Forms");
            TryLoadType("System.Drawing.Point, System.Drawing");

            static void TryLoadType(string typeName)
            {
                try
                {
                    _ = Type.GetType(typeName, throwOnError: false);
                }
                catch
                {
                    // ignore failures; the compilation step will surface missing assemblies
                }
            }
        }
        private static void TryAddWinFormsReferences(List<MetadataReference> references)
        {
            if (references == null) throw new ArgumentNullException(nameof(references));

            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var requiredAssemblies = new[]
            {
                "System.Windows.Forms",
                "System.Drawing",
                "System.Drawing.Common",
                "Microsoft.Win32.SystemEvents"
            };

            var missing = new List<string>();

            foreach (var assemblyName in requiredAssemblies)
            {
                if (!TryAddFromLoadedContext(assemblyName))
                {
                    missing.Add(assemblyName);
                }
            }

            if (missing.Count > 0)
            {
                foreach (var pathRef in GetWindowsDesktopReferencePaths(missing))
                {
                    AddReferenceFromFile(pathRef);
                }
            }

            bool TryAddFromLoadedContext(string assemblyName)
            {
                try
                {
                    var assembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(asm => string.Equals(asm.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                        ?? Assembly.Load(assemblyName);

                    if (assembly != null && !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    {
                        AddReferenceFromFile(assembly.Location);
                        return true;
                    }
                }
                catch
                {
                    // ignore load failures; we will try reference assemblies as a fallback
                }

                return false;
            }

            void AddReferenceFromFile(string pathRef)
            {
                if (string.IsNullOrWhiteSpace(pathRef) || !File.Exists(pathRef))
                {
                    return;
                }

                if (references.OfType<PortableExecutableReference>()
                    .Any(r => string.Equals(r.FilePath, pathRef, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                references.Add(MetadataReference.CreateFromFile(pathRef));
            }
        }

        private static List<string> BuildPackageReferenceLines(string nuget, string normalizedProjectType)
        {
            var map = BuildPackageReferenceMap(nuget, normalizedProjectType);
            return map
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => string.IsNullOrWhiteSpace(entry.Value)
                    ? $"    <PackageReference Include=\"{entry.Key}\" />"
                    : $"    <PackageReference Include=\"{entry.Key}\" Version=\"{entry.Value}\" />")
                .ToList();
        }


        private static Dictionary<string, string?> BuildPackageReferenceMap(string nuget, string normalizedProjectType)
        {
            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            void Add(string? id, string? version)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return;
                }

                var normalizedId = id.Trim();
                var normalizedVersion = NormalizePackageVersion(version);

                if (map.TryGetValue(normalizedId, out var existing))
                {
                    if (string.IsNullOrWhiteSpace(existing) && !string.IsNullOrWhiteSpace(normalizedVersion))
                    {
                        map[normalizedId] = normalizedVersion;
                    }

                    return;
                }

                map[normalizedId] = normalizedVersion;
            }

            if (!string.IsNullOrWhiteSpace(nuget))
            {
                foreach (var part in nuget.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var items = part.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var id = items.Length > 0 ? items[0].Trim() : null;
                    var version = items.Length > 1 ? items[1].Trim() : null;
                    Add(id, version);
                }
            }

            void AddDefaultPackages()
            {
                switch (normalizedProjectType)
                {
                    case "aspnetcore":
                    case "aspnetcorewebapi":
                    case "webapi":
                    case "web":
                        Add("Microsoft.AspNetCore.Mvc.NewtonsoftJson", TryGetPackageVersionFromAssembly("Microsoft.AspNetCore.Mvc.NewtonsoftJson", "8.0.11"));
                        Add("Newtonsoft.Json", TryGetPackageVersionFromAssembly("Newtonsoft.Json", "13.0.3"));
                        Add("Swashbuckle.AspNetCore.Swagger", TryGetPackageVersionFromAssembly("Swashbuckle.AspNetCore.Swagger", "6.8.1"));
                        Add("Swashbuckle.AspNetCore.SwaggerGen", TryGetPackageVersionFromAssembly("Swashbuckle.AspNetCore.SwaggerGen", "6.8.1"));
                        Add("Swashbuckle.AspNetCore.SwaggerUI", TryGetPackageVersionFromAssembly("Swashbuckle.AspNetCore.SwaggerUI", "6.8.1"));
                        break;
                }
            }

            AddDefaultPackages();
            return map;
        }

        private static string? NormalizePackageVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var trimmed = version.Trim();
            var plusIndex = trimmed.IndexOf('+');
            if (plusIndex >= 0)
            {
                trimmed = trimmed[..plusIndex];
            }

            return trimmed.Length == 0 ? null : trimmed;
        }

        private static string? TryGetPackageVersionFromAssembly(string assemblyName, string? fallbackVersion = null)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return NormalizePackageVersion(fallbackVersion);
            }

            try
            {
                var assembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => !a.IsDynamic && string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    ?? Assembly.Load(new AssemblyName(assemblyName));

                if (assembly != null)
                {
                    string? version = null;

                    if (!string.IsNullOrWhiteSpace(assembly.Location))
                    {
                        try
                        {
                            var info = FileVersionInfo.GetVersionInfo(assembly.Location);
                            version = NormalizePackageVersion(info.ProductVersion) ?? NormalizePackageVersion(info.FileVersion);
                        }
                        catch
                        {
                            // ignore version probing failures
                        }
                    }

                    version ??= NormalizePackageVersion(assembly.GetName().Version?.ToString());

                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return version;
                    }
                }
            }
            catch
            {
                // ignore load failures
            }

            return NormalizePackageVersion(fallbackVersion);
        }


        private static void TryEnsureAssemblyLoaded(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return;
            }

            try
            {
                var loaded = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Any(a => !a.IsDynamic && string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));

                if (!loaded)
                {
                    Assembly.Load(new AssemblyName(assemblyName));
                }
            }
            catch
            {
                // ignore load failures; references may not be required at runtime
            }
        }

        private static void CopyAssembliesForPackages(IEnumerable<string> packageIds, string workingDirectory, HashSet<string> copiedFiles)
        {
            if (packageIds == null)
            {
                return;
            }

            foreach (var packageId in packageIds)
            {
                CopyAssemblyByName(packageId, workingDirectory, copiedFiles);
            }
        }

        private static void CopyAssemblyByName(string assemblyName, string workingDirectory, HashSet<string> copiedFiles)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return;
            }

            Assembly? assembly = null;
            try
            {
                assembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => !a.IsDynamic && string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));

                assembly ??= Assembly.Load(new AssemblyName(assemblyName));
            }
            catch
            {
                assembly = null;
            }

            if (assembly == null || string.IsNullOrWhiteSpace(assembly.Location))
            {
                return;
            }

            try
            {
                CopyAssemblyFile(assembly.Location, workingDirectory, copiedFiles);

                var directory = Path.GetDirectoryName(assembly.Location);
                if (!string.IsNullOrEmpty(directory))
                {
                    foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        CopyAssemblyFile(file, workingDirectory, copiedFiles);
                    }
                }
            }
            catch
            {
                // ignore copy failures
            }
        }

        private static void CopyHostAssembliesForExecution(string workingDirectory, HashSet<string> copiedFiles)
        {
            var baseDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return;
            }

            string? normalizedBase;
            try
            {
                normalizedBase = Path.GetFullPath(baseDirectory);
            }
            catch
            {
                normalizedBase = null;
            }

            if (string.IsNullOrEmpty(normalizedBase))
            {
                return;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                {
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(assembly.Location);
                }
                catch
                {
                    continue;
                }

                if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CopyAssemblyFile(fullPath, workingDirectory, copiedFiles);
            }
        }

        private static void CopyAssemblyFile(string sourcePath, string workingDirectory, HashSet<string> copiedFiles)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return;
            }

            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            var destination = Path.Combine(workingDirectory, fileName);
            if (!copiedFiles.Add(destination))
            {
                return;
            }

            try
            {
                File.Copy(sourcePath, destination, overwrite: true);
            }
            catch
            {
                copiedFiles.Remove(destination);
            }
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

            var packages = BuildPackageReferenceLines(nuget, normalizedType);
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

        public static void DownloadPackage(string nuget, string preferredSourceKey = null)
        {
            DownloadNugetPackages.DownloadAllPackagesAsync(nuget, preferredSourceKey).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Removes NuGet packages from local cache
        /// </summary>
        /// <param name="nuget">Semicolon-separated list of packages in "name,version" format</param>
        public static void RemovePackages(string nuget)
        {
            CompletionWorkspace.ClearReferenceCache();
            DownloadNugetPackages.RemoveAllPackages(nuget);
        }

        public static async Task<ExeBuildResult> BuildMultiFileExecutableAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            string outputFileName,
            string projectType,
            Func<string, Task> onOutput = null)
        {
            return await BuildWithDotnetPublishAsync(files, nuget, languageVersion, outputFileName, projectType, onOutput);
        }

        public static async Task<ExeBuildResult> BuildExecutableAsync(
            string code,
            string nuget,
            int languageVersion,
            string outputFileName,
            string projectType,
            Func<string, Task> onOutput = null)
        {
            var files = new List<FileContent> { new FileContent { FileName = "Program.cs", Content = code } };
            return await BuildWithDotnetPublishAsync(files, nuget, languageVersion, outputFileName, projectType, onOutput);
        }

        private static async Task<ExeBuildResult> BuildWithDotnetPublishAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            string outputFileName,
            string projectType,
            Func<string, Task> onOutput = null)
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

                var normalizedProjectType = NormalizeProjectType(projectType);
                var sdk = "Microsoft.NET.Sdk";
                var targetFramework = GetHostTargetFramework(requireWindows: false);
                var outputTypeValue = "Exe";
                var propertyExtraLines = new List<string>();
                var frameworkRefLines = new List<string>();
                switch (normalizedProjectType)
                {
                    case "winform":
                    case "winforms":
                    case "windowsforms":
                        targetFramework = GetHostTargetFramework(requireWindows: true);
                        outputTypeValue = "WinExe";
                        propertyExtraLines.Add("    <UseWindowsForms>true</UseWindowsForms>");
                        propertyExtraLines.Add("    <EnableWindowsTargeting>true</EnableWindowsTargeting>");
                        frameworkRefLines.Add("    <FrameworkReference Include=\"Microsoft.WindowsDesktop.App\" />");
                        break;
                    case "aspnetcore":
                    case "aspnetcorewebapi":
                    case "webapi":
                    case "web":
                        sdk = "Microsoft.NET.Sdk.Web";
                        break;
                    default:
                        break;
                }

                var csprojPath = Path.Combine(srcDir, $"{asmName}.csproj");

                var packageReferences = BuildPackageReferenceMap(nuget, normalizedProjectType);

                var langVer = "latest";
                try { langVer = ((LanguageVersion)languageVersion).ToString(); } catch { }

                var projectBuilder = new StringBuilder();
                projectBuilder.AppendLine($"<Project Sdk=\"{sdk}\">");
                projectBuilder.AppendLine("  <PropertyGroup>");
                projectBuilder.AppendLine($"    <OutputType>{outputTypeValue}</OutputType>");
                projectBuilder.AppendLine($"    <TargetFramework>{targetFramework}</TargetFramework>");
                projectBuilder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
                projectBuilder.AppendLine("    <Nullable>enable</Nullable>");
                projectBuilder.AppendLine($"    <AssemblyName>{asmName}</AssemblyName>");
                projectBuilder.AppendLine($"    <RootNamespace>{asmName}</RootNamespace>");
                projectBuilder.AppendLine($"    <LangVersion>{langVer}</LangVersion>");
                foreach (var line in propertyExtraLines)
                {
                    projectBuilder.AppendLine(line);
                }
                projectBuilder.AppendLine("  </PropertyGroup>");

                if (packageReferences.Count > 0)
                {
                    projectBuilder.AppendLine("  <ItemGroup>");
                    foreach (var reference in packageReferences.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(reference.Value))
                        {
                            projectBuilder.AppendLine($"    <PackageReference Include=\"{reference.Key}\" />");
                        }
                        else
                        {
                            projectBuilder.AppendLine($"    <PackageReference Include=\"{reference.Key}\" Version=\"{reference.Value}\" />");
                        }
                    }
                    projectBuilder.AppendLine("  </ItemGroup>");
                }

                if (frameworkRefLines.Count > 0)
                {
                    projectBuilder.AppendLine("  <ItemGroup>");
                    foreach (var line in frameworkRefLines)
                    {
                        projectBuilder.AppendLine(line);
                    }
                    projectBuilder.AppendLine("  </ItemGroup>");
                }

                projectBuilder.AppendLine("</Project>");
                var csproj = projectBuilder.ToString();
                await File.WriteAllTextAsync(csprojPath, csproj, Encoding.UTF8);

                foreach (var f in files)
                {
                    var safeName = string.IsNullOrWhiteSpace(f?.FileName) ? "Program.cs" : f.FileName;
                    foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
                    var dest = Path.Combine(srcDir, safeName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await File.WriteAllTextAsync(dest, f?.Content ?? string.Empty, Encoding.UTF8);
                }

                async Task<(int code, string stdout, string stderr)> RunAsync(string fileName, string args, string workingDir)
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
                    p.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data != null)
                        {
                            sbOut.AppendLine(e.Data);
                            onOutput?.Invoke(e.Data + Environment.NewLine).GetAwaiter().GetResult();
                        }
                    };
                    p.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data != null)
                        {
                            sbErr.AppendLine(e.Data);
                            onOutput?.Invoke(e.Data + Environment.NewLine).GetAwaiter().GetResult();
                        }
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    await p.WaitForExitAsync();
                    return (p.ExitCode, sbOut.ToString(), sbErr.ToString());
                }

                if (onOutput != null)
                {
                    await onOutput($"开始还原 NuGet 包...\n");
                }
                var (rc1, o1, e1) = await RunAsync("dotnet", "restore", srcDir);
                if (rc1 != 0)
                {
                    var msg = $"dotnet restore failed.\n{o1}\n{e1}";
                    result.Success = false;
                    result.Error = msg;
                    return result;
                }

                if (onOutput != null)
                {
                    await onOutput($"开始发布应用...\n");
                }
                var rid = OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";
                var publishArgs = $"publish -c Release -r {rid} --self-contained true -o \"{publishDir}\"";
                var (rc2, o2, e2) = await RunAsync("dotnet", publishArgs, srcDir);
                if (rc2 != 0)
                {
                    var msg = $"dotnet publish failed.\n{o2}\n{e2}";
                    result.Success = false;
                    result.Error = msg;
                    return result;
                }

                if (onOutput != null)
                {
                    await onOutput($"创建发布包...\n");
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

    }

    // 立即回调的 TextWriter，用于进程内执行
    internal sealed class ImmediateCallbackTextWriter : TextWriter
    {
        private readonly Func<string, Task> _onWrite;

        public ImmediateCallbackTextWriter(Func<string, Task> onWrite)
        {
            _onWrite = onWrite ?? throw new ArgumentNullException(nameof(onWrite));
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => _onWrite(value.ToString()).GetAwaiter().GetResult();

        public override void Write(string value) => _onWrite(value ?? string.Empty).GetAwaiter().GetResult();

        public override void WriteLine(string value) => _onWrite((value ?? string.Empty) + Environment.NewLine).GetAwaiter().GetResult();

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // 自定义可卸载的AssemblyLoadContext
    internal sealed class CustomAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, PackageAssemblyInfo> _packageAssemblies;

        public CustomAssemblyLoadContext(IEnumerable<PackageAssemblyInfo> packageAssemblies) : base(isCollectible: true)
        {
            _packageAssemblies = packageAssemblies?
                .GroupBy(p => p.AssemblyName.Name ?? Path.GetFileNameWithoutExtension(p.Path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.AssemblyName.Version ?? new Version(0, 0, 0, 0)).First(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, PackageAssemblyInfo>(StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (assemblyName?.Name != null && _packageAssemblies.TryGetValue(assemblyName.Name, out var package))
            {
                return LoadFromAssemblyPath(package.Path);
            }

            return null;
        }
    }
}





