using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using monacoEditorCSharp.DataHelpers;
using System.Runtime.Loader;
using System.Threading;
using System.Diagnostics;
using System.IO.Compression;

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeRunner
    {
        // 用于同步Console重定向的锁对象
        private static readonly object ConsoleLock = new object();

        // 用于创建AssemblyLoadContext的锁对象
        private static readonly object LoadContextLock = new object();

        // 存储正在运行的代码的交互式读取器
        private static readonly Dictionary<string, InteractiveTextReader> _activeReaders = new();

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
            if (!requiresStaThread)
            {
                return Task.Run(executeAsync);
            }

            var tcs = new TaskCompletionSource<object?>();
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
                IsBackground = true
            };

            try
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            catch (PlatformNotSupportedException)
            {
                return Task.Run(executeAsync);
            }

            thread.Start();
            return tcs.Task;
        }

        public static bool ProvideInput(string sessionId, string input)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;

            lock (_activeReaders)
            {
                if (_activeReaders.TryGetValue(sessionId, out var reader))
                {
                    reader.ProvideInput(input);
                    return true;
                }
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
            string projectType = null)
        {
            var result = new RunResult();
            CustomAssemblyLoadContext loadContext = null;
            Assembly assembly = null;
            try
            {
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);
                loadContext = new CustomAssemblyLoadContext(nugetAssemblies);

                var runBehavior = GetRunBehavior(projectType);
                if (runBehavior.OutputKind != OutputKind.WindowsApplication && DetectWinFormsUsage(files?.Select(f => f?.Content)))
                {
                    // 自动检测到 WinForms 代码时启用所需的运行时设置
                    runBehavior = (OutputKind.WindowsApplication, true);
                }

                var parseOptions = new CSharpParseOptions(
                    languageVersion: (LanguageVersion)languageVersion,
                    kind: SourceCodeKind.Regular,
                    documentationMode: DocumentationMode.Parse
                );

                string assemblyName = "DynamicCode";

                // Parse all files into syntax trees
                var syntaxTrees = new List<SyntaxTree>();
                foreach (var file in files)
                {
                    var syntaxTree = CSharpSyntaxTree.ParseText(
                        file.Content,
                        parseOptions,
                        path: file.FileName
                    );
                    syntaxTrees.Add(syntaxTree);
                }

                // Collect references
                var references = new List<MetadataReference>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                        references.Add(MetadataReference.CreateFromFile(asm.Location));
                }

                // 确保加载 Windows Forms 引用（如果是 Windows Forms 项目）
                if (runBehavior.OutputKind == OutputKind.WindowsApplication)
                {
                    EnsureWinFormsAssembliesLoaded();
                    // 尝试添加 Windows Forms 相关的程序集引用
                    TryAddWinFormsReferences(references);
                }

                foreach (var pkg in nugetAssemblies)
                {
                    references.Add(MetadataReference.CreateFromFile(pkg.Path));
                }

                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(runBehavior.OutputKind)
                );

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

                    lock (LoadContextLock)
                    {
                        assembly = loadContext.LoadFromStream(peStream);
                    }

                    var entryPoint = assembly.EntryPoint;
                    if (entryPoint != null)
                    {
                        var parameters = entryPoint.GetParameters();

                        async void WriteAction(string text) => await onOutput(text).ConfigureAwait(false);
                        await using var outputWriter = new ImmediateCallbackTextWriter(WriteAction);

                        async void ErrorAction(string text) => await onError(text).ConfigureAwait(false);
                        await using var errorWriter = new ImmediateCallbackTextWriter(ErrorAction);

                        var interactiveReader = new InteractiveTextReader(async prompt =>
                        {
                            await onOutput($"[INPUT REQUIRED] Please provide input: ").ConfigureAwait(false);
                        });

                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            lock (_activeReaders)
                            {
                                _activeReaders[sessionId] = interactiveReader;
                            }
                        }

                        Func<Task> executeAsync = async () =>
                        {
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
                                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
                                {
                                    entryPoint.Invoke(null, new object[] { new string[] { "sharpPad" } });
                                }
                                else
                                {
                                    entryPoint.Invoke(null, null);
                                }
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
                                    lock (_activeReaders)
                                    {
                                        _activeReaders.Remove(sessionId);
                                    }
                                }
                                interactiveReader?.Dispose();
                            }
                        };

                        await RunEntryPointAsync(executeAsync, runBehavior.RequiresStaThread).ConfigureAwait(false);
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

            result.Output = string.Empty;
            result.Error = string.Empty;
            return result;
        }

        public static async Task<RunResult> RunProgramCodeAsync(
    string code,
    string nuget,
    int languageVersion,
    Func<string, Task> onOutput,
    Func<string, Task> onError,
    string sessionId = null,
    string projectType = null)
        {
            var result = new RunResult();
            // 低内存版本：不使用 StringBuilder 缓存输出，只依赖回调传递实时数据
            CustomAssemblyLoadContext loadContext = null;
            Assembly assembly = null;
            try
            {
                // 加载 NuGet 包（假设返回的包集合较小）
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);
                loadContext = new CustomAssemblyLoadContext(nugetAssemblies);

                var runBehavior = GetRunBehavior(projectType);
                if (runBehavior.OutputKind != OutputKind.WindowsApplication && DetectWinFormsUsage(code))
                {
                    // 自动检测到 WinForms 代码时启用所需的运行时设置
                    runBehavior = (OutputKind.WindowsApplication, true);
                }

                // 设置解析选项，尽可能减小额外开销
                var parseOptions = new CSharpParseOptions(
                    languageVersion: (LanguageVersion)languageVersion,
                    kind: SourceCodeKind.Regular,
                    documentationMode: DocumentationMode.Parse
                );

                string assemblyName = "DynamicCode";

                // 解析代码
                var syntaxTree = CSharpSyntaxTree.ParseText(code, parseOptions);

                // 用循环收集引用，避免 LINQ 的额外内存分配
                var references = new List<MetadataReference>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                        references.Add(MetadataReference.CreateFromFile(asm.Location));
                }

                // 确保加载 Windows Forms 引用（如果是 Windows Forms 项目）
                if (runBehavior.OutputKind == OutputKind.WindowsApplication)
                {
                    EnsureWinFormsAssembliesLoaded();
                    // 尝试添加 Windows Forms 相关的程序集引用
                    TryAddWinFormsReferences(references);
                }

                // 添加 NuGet 包引用
                foreach (var pkg in nugetAssemblies)
                {
                    references.Add(MetadataReference.CreateFromFile(pkg.Path));
                }

                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(runBehavior.OutputKind)
                );

                // 编译到内存流，使用完后尽快释放内存
                using (var peStream = new MemoryStream())
                {
                    var compileResult = compilation.Emit(peStream);
                    if (!compileResult.Success)
                    {
                        foreach (var diag in compileResult.Diagnostics)
                        {
                            if (diag.Severity == DiagnosticSeverity.Error)
                                await onError(diag.ToString()).ConfigureAwait(false);
                        }
                        result.Error = "Compilation error";
                        return result;
                    }
                    peStream.Seek(0, SeekOrigin.Begin);

                    // 加载程序集（使用锁保证并发安全）
                    lock (LoadContextLock)
                    {
                        assembly = loadContext.LoadFromStream(peStream);
                    }

                    var entryPoint = assembly.EntryPoint;
                    if (entryPoint != null)
                    {
                        var parameters = entryPoint.GetParameters();

                        // 定义直接回调写入器，不做缓存
                        async void WriteAction(string text) => await onOutput(text).ConfigureAwait(false);
                        await using var outputWriter = new ImmediateCallbackTextWriter(WriteAction);

                        async void ErrorAction(string text) => await onError(text).ConfigureAwait(false);
                        await using var errorWriter = new ImmediateCallbackTextWriter(ErrorAction);

                        // 创建交互式输入读取器
                        var interactiveReader = new InteractiveTextReader(async prompt =>
                        {
                            await onOutput($"[INPUT REQUIRED] Please provide input: ").ConfigureAwait(false);
                        });

                        // 如果提供了会话ID，将读取器存储起来以便后续提供输入
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            lock (_activeReaders)
                            {
                                _activeReaders[sessionId] = interactiveReader;
                            }
                        }

                        // 异步执行入口点代码
                        Func<Task> executeAsync = async () =>
                        {
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
                                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
                                {
                                    // 兼容 Main(string[] args)
                                    entryPoint.Invoke(null, new object[] { new string[] { "sharpPad" } });
                                }
                                else
                                {
                                    entryPoint.Invoke(null, null);
                                }
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
                                
                                // 清理会话
                                if (!string.IsNullOrEmpty(sessionId))
                                {
                                    lock (_activeReaders)
                                    {
                                        _activeReaders.Remove(sessionId);
                                    }
                                }
                                interactiveReader?.Dispose();
                            }
                        };

                        await RunEntryPointAsync(executeAsync, runBehavior.RequiresStaThread).ConfigureAwait(false);
                    }
                    else
                    {
                        await onError("No entry point found in the code.").ConfigureAwait(false);
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
                // 卸载程序集并强制垃圾回收，尽快释放内存
                loadContext?.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            // 低内存版本不缓存输出，返回空字符串
            result.Output = string.Empty;
            result.Error = string.Empty;
            return result;
        }


        // 自定义可卸载的AssemblyLoadContext
        private class CustomAssemblyLoadContext : AssemblyLoadContext
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

        private class ImmediateCallbackTextWriter : TextWriter
        {
            private readonly Action<string> _writeAction;

            public ImmediateCallbackTextWriter(Action<string> writeAction)
            {
                _writeAction = writeAction ?? throw new ArgumentNullException(nameof(writeAction));
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                _writeAction(value.ToString());
            }

            public override void Write(char[] buffer, int index, int count)
            {
                if (buffer == null)
                    throw new ArgumentNullException(nameof(buffer));
                _writeAction(new string(buffer, index, count));
            }

            public override void Write(string value)
            {
                if (value != null)
                    _writeAction(value);
            }

            public override void WriteLine(string value)
            {
                _writeAction((value ?? string.Empty) + Environment.NewLine);
            }

            public override Task WriteAsync(char value)
            {
                _writeAction(value.ToString());
                return Task.CompletedTask;
            }

            public override Task WriteAsync(string value)
            {
                Write(value);
                return Task.CompletedTask;
            }

            public override Task WriteLineAsync(string value)
            {
                WriteLine(value);
                return Task.CompletedTask;
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
            TryLoadAssembly("System.Windows.Forms");
            TryLoadAssembly("System.Drawing");
            TryLoadAssembly("System.Drawing.Common");

            static void TryLoadAssembly(string assemblyName)
            {
                try
                {
                    Assembly.Load(assemblyName);
                }
                catch
                {
                    // ignore when the assembly can't be loaded in the current environment
                }
            }
        }

        private static void TryAddWinFormsReferences(List<MetadataReference> references)
        {
            // Windows Forms 相关的程序集名称
            var winFormsAssemblies = new[]
            {
                "System.Windows.Forms",
                "System.Drawing",
                "System.Drawing.Common",
                "System.Drawing.Primitives"
            };

            foreach (var assemblyName in winFormsAssemblies)
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    if (!string.IsNullOrEmpty(assembly.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(assembly.Location));
                    }
                }
                catch
                {
                    // 如果无法加载某个程序集，忽略并继续
                    try
                    {
                        // 尝试从当前域中查找已加载的程序集
                        var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(asm => asm.GetName().Name?.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) == true);

                        if (loadedAssembly != null && !loadedAssembly.IsDynamic && !string.IsNullOrEmpty(loadedAssembly.Location))
                        {
                            references.Add(MetadataReference.CreateFromFile(loadedAssembly.Location));
                        }
                    }
                    catch
                    {
                        // 最终失败也忽略
                    }
                }
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

        private static string BuildProjectFileContent(string assemblyName, string nuget, int languageVersion, string projectType)
        {
            var sdk = "Microsoft.NET.Sdk";
            var targetFramework = "net9.0";
            var outputTypeValue = "Exe";
            var propertyExtraLines = new List<string>();

            switch (projectType)
            {
                case "winforms":
                    targetFramework = "net9.0-windows";
                    outputTypeValue = "WinExe";
                    propertyExtraLines.Add("    <UseWindowsForms>true</UseWindowsForms>");
                    propertyExtraLines.Add("    <EnableWindowsTargeting>true</EnableWindowsTargeting>");
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

                var normalizedProjectType = NormalizeProjectType(projectType);
                var sdk = "Microsoft.NET.Sdk";
                var targetFramework = "net9.0";
                var outputTypeValue = "Exe";
                var propertyExtraLines = new List<string>();
                var frameworkRefLines = new List<string>();

                switch (normalizedProjectType)
                {
                    case "winform":
                    case "winforms":
                    case "windowsforms":
                        targetFramework = "net9.0-windows";
                        outputTypeValue = "WinExe";
                        propertyExtraLines.Add("    <UseWindowsForms>true</UseWindowsForms>");
                        propertyExtraLines.Add("    <EnableWindowsTargeting>true</EnableWindowsTargeting>");
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

                var pkgRefs = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(nuget))
                {
                    foreach (var part in nuget.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var items = part.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        var id = items.Length > 0 ? items[0].Trim() : null;
                        var ver = items.Length > 1 ? items[1].Trim() : null;
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            if (!string.IsNullOrWhiteSpace(ver))
                                pkgRefs.AppendLine($"    <PackageReference Include=\"{id}\" Version=\"{ver}\" />");
                            else
                                pkgRefs.AppendLine($"    <PackageReference Include=\"{id}\" />");
                        }
                    }
                }

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

                var pkgRefText = pkgRefs.ToString().TrimEnd();
                if (!string.IsNullOrEmpty(pkgRefText))
                {
                    projectBuilder.AppendLine("  <ItemGroup>");
                    projectBuilder.AppendLine(pkgRefText);
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
}


