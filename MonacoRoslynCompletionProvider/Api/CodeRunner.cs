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
            string sessionId = null)
        {
            var result = new RunResult();
            CustomAssemblyLoadContext loadContext = null;
            Assembly assembly = null;
            try
            {
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);
                loadContext = new CustomAssemblyLoadContext(nugetAssemblies);

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
                foreach (var pkg in nugetAssemblies)
                {
                    references.Add(MetadataReference.CreateFromFile(pkg.Path));
                }

                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(OutputKind.ConsoleApplication)
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

                        var executionTask = Task.Run(async () =>
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
                        });
                        await executionTask.ConfigureAwait(false);
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
    string sessionId = null)
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
                // 添加 NuGet 包引用
                foreach (var pkg in nugetAssemblies)
                {
                    references.Add(MetadataReference.CreateFromFile(pkg.Path));
                }

                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(OutputKind.ConsoleApplication)
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
                        var executionTask = Task.Run(async () =>
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
                        });
                        await executionTask.ConfigureAwait(false);
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



        public static void DownloadPackage(string nuget)
        {
            DownloadNugetPackages.DownloadAllPackages(nuget);
        }

        public static async Task<ExeBuildResult> BuildMultiFileExecutableAsync(
            List<FileContent> files,
            string nuget,
            int languageVersion,
            string outputFileName)
        {
            var result = new ExeBuildResult();

            try
            {
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);

                var parseOptions = new CSharpParseOptions(
                    languageVersion: (LanguageVersion)languageVersion,
                    kind: SourceCodeKind.Regular,
                    documentationMode: DocumentationMode.Parse
                );

                string assemblyName = Path.GetFileNameWithoutExtension(outputFileName);

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
                foreach (var pkg in nugetAssemblies)
                {
                    references.Add(MetadataReference.CreateFromFile(pkg.Path));
                }

                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(
                        OutputKind.ConsoleApplication,
                        optimizationLevel: OptimizationLevel.Release,
                        platform: Platform.AnyCpu
                    )
                );

                // Create output directory if it doesn't exist
                var outputDir = Path.Combine(Path.GetTempPath(), "SharpPadBuilds");
                Directory.CreateDirectory(outputDir);

                var outputPath = Path.Combine(outputDir, outputFileName);

                // Generate exe and dependencies
                var emitResult = compilation.Emit(outputPath);

                if (emitResult.Success)
                {
                    // Copy dependencies to output directory
                    await CopyDependenciesToOutputAsync(outputPath, nugetAssemblies);

                    // Create zip package
                    var zipPath = await CreateZipPackageAsync(outputPath, nugetAssemblies);

                    result.Success = true;
                    result.ExeFilePath = zipPath;
                    result.FileSizeBytes = new FileInfo(zipPath).Length;
                    result.CompilationMessages.Add($"Successfully built {outputFileName} and packaged dependencies");
                }
                else
                {
                    result.Success = false;
                    foreach (var diagnostic in emitResult.Diagnostics)
                    {
                        if (diagnostic.Severity == DiagnosticSeverity.Error)
                        {
                            var location = diagnostic.Location.GetLineSpan();
                            var fileName = location.Path ?? "unknown";
                            var line = location.StartLinePosition.Line + 1;
                            result.CompilationMessages.Add($"[{fileName}:{line}] {diagnostic.GetMessage()}");
                        }
                    }
                    result.Error = "Compilation failed with errors";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Build error: {ex.Message}";
            }

            return result;
        }

        public static async Task<ExeBuildResult> BuildExecutableAsync(
            string code,
            string nuget,
            int languageVersion,
            string outputFileName)
        {
            var result = new ExeBuildResult();

            try
            {
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);

                var parseOptions = new CSharpParseOptions(
                    languageVersion: (LanguageVersion)languageVersion,
                    kind: SourceCodeKind.Regular,
                    documentationMode: DocumentationMode.Parse
                );

                string assemblyName = Path.GetFileNameWithoutExtension(outputFileName);

                var syntaxTree = CSharpSyntaxTree.ParseText(code, parseOptions);

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
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(
                        OutputKind.ConsoleApplication,
                        optimizationLevel: OptimizationLevel.Release,
                        platform: Platform.AnyCpu
                    )
                );

                // Create output directory if it doesn't exist
                var outputDir = Path.Combine(Path.GetTempPath(), "SharpPadBuilds");
                Directory.CreateDirectory(outputDir);

                var outputPath = Path.Combine(outputDir, outputFileName);

                // Generate exe and dependencies
                var emitResult = compilation.Emit(outputPath);

                if (emitResult.Success)
                {
                    // Copy dependencies to output directory
                    await CopyDependenciesToOutputAsync(outputPath, nugetAssemblies);

                    // Create zip package
                    var zipPath = await CreateZipPackageAsync(outputPath, nugetAssemblies);

                    result.Success = true;
                    result.ExeFilePath = zipPath;
                    result.FileSizeBytes = new FileInfo(zipPath).Length;
                    result.CompilationMessages.Add($"Successfully built {outputFileName} and packaged dependencies");
                }
                else
                {
                    result.Success = false;
                    foreach (var diagnostic in emitResult.Diagnostics)
                    {
                        if (diagnostic.Severity == DiagnosticSeverity.Error)
                        {
                            result.CompilationMessages.Add(diagnostic.ToString());
                        }
                    }
                    result.Error = "Compilation failed with errors";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Build error: {ex.Message}";
            }

            return result;
        }

        private static async Task CopyDependenciesToOutputAsync(string exePath, IEnumerable<PackageAssemblyInfo> nugetAssemblies)
        {
            var outputDir = Path.GetDirectoryName(exePath);

            // 复制NuGet依赖
            foreach (var assembly in nugetAssemblies)
            {
                var fileName = Path.GetFileName(assembly.Path);
                var destPath = Path.Combine(outputDir, fileName);
                try
                {
                    File.Copy(assembly.Path, destPath, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not copy dependency {fileName}: {ex.Message}");
                }
            }

            // 创建运行脚本
            var scriptContent = $@"@echo off
echo Starting {Path.GetFileName(exePath)}...
""{Path.GetFileName(exePath)}""
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Program exited with error code %ERRORLEVEL%
    pause
)";
            var scriptPath = Path.Combine(outputDir, "run.bat");
            await File.WriteAllTextAsync(scriptPath, scriptContent);
        }

        private static async Task<string> CreateZipPackageAsync(string exePath, IEnumerable<PackageAssemblyInfo> nugetAssemblies)
        {
            var outputDir = Path.GetDirectoryName(exePath);
            var zipFileName = Path.GetFileNameWithoutExtension(exePath) + "_Package.zip";
            var zipPath = Path.Combine(outputDir, zipFileName);

            // 删除已存在的zip文件
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using (var zip = new System.IO.Compression.ZipArchive(File.Create(zipPath), System.IO.Compression.ZipArchiveMode.Create))
            {
                // 添加主程序
                if (File.Exists(exePath))
                {
                    var entry = zip.CreateEntry(Path.GetFileName(exePath));
                    using (var entryStream = entry.Open())
                    using (var fileStream = File.OpenRead(exePath))
                    {
                        await fileStream.CopyToAsync(entryStream);
                    }
                }

                // 添加依赖文件
                foreach (var assembly in nugetAssemblies)
                {
                    var fileName = Path.GetFileName(assembly.Path);
                    var destFileName = Path.Combine(outputDir, fileName);

                    if (File.Exists(destFileName))
                    {
                        var entry = zip.CreateEntry(fileName);
                        using (var entryStream = entry.Open())
                        using (var fileStream = File.OpenRead(destFileName))
                        {
                            await fileStream.CopyToAsync(entryStream);
                        }
                    }
                }

                // 添加运行脚本
                var scriptPath = Path.Combine(outputDir, "run.bat");
                if (File.Exists(scriptPath))
                {
                    var entry = zip.CreateEntry("run.bat");
                    using (var entryStream = entry.Open())
                    using (var fileStream = File.OpenRead(scriptPath))
                    {
                        await fileStream.CopyToAsync(entryStream);
                    }
                }

                // 添加说明文件
                var readmeEntry = zip.CreateEntry("README.txt");
                using (var entryStream = readmeEntry.Open())
                using (var writer = new StreamWriter(entryStream))
                {
                    await writer.WriteLineAsync("SharpPad Generated Application Package");
                    await writer.WriteLineAsync("=====================================");
                    await writer.WriteLineAsync("");
                    await writer.WriteLineAsync("This package contains a compiled C# console application and its dependencies.");
                    await writer.WriteLineAsync("");
                    await writer.WriteLineAsync("To run the application:");
                    await writer.WriteLineAsync("1. Extract all files to a folder");
                    await writer.WriteLineAsync("2. Double-click 'run.bat' to start the application");
                    await writer.WriteLineAsync($"3. Or directly run '{Path.GetFileName(exePath)}'");
                    await writer.WriteLineAsync("");
                    await writer.WriteLineAsync("Requirements:");
                    await writer.WriteLineAsync("- .NET Runtime (compatible with the target framework)");
                    await writer.WriteLineAsync("");
                    await writer.WriteLineAsync("Generated by SharpPad - https://github.com/gaoconggit/SharpPad");
                }
            }

            return zipPath;
        }
    }
}
