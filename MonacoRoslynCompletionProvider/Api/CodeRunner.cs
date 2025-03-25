using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis;
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
        private static readonly Lock ConsoleLock = new Lock();

        // 用于创建AssemblyLoadContext的锁对象
        private static readonly Lock LoadContextLock = new Lock();

        public class RunResult
        {
            public string Output { get; set; }
            public string Error { get; set; }
        }

        public static async Task<RunResult> RunProgramCodeAsync(
    string code,
    string nuget,
    int languageVersion,
    Func<string, Task> onOutput,
    Func<string, Task> onError)
        {
            var result = new RunResult();
            // 低内存版本：不使用 StringBuilder 缓存输出，只依赖回调传递实时数据
            var loadContext = new CustomAssemblyLoadContext();
            Assembly assembly = null;
            try
            {
                // 加载 NuGet 包（假设返回的包集合较小）
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);

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
                    references.Add(MetadataReference.CreateFromFile(pkg.Location));
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

                        // 异步执行入口点代码
                        var executionTask = Task.Run(async () =>
                        {
                            TextWriter originalOut = null, originalError = null;
                            lock (ConsoleLock)
                            {
                                originalOut = Console.Out;
                                originalError = Console.Error;
                                Console.SetOut(outputWriter);
                                Console.SetError(errorWriter);
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
                                }
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
                loadContext.Unload();
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
            public CustomAssemblyLoadContext() : base(isCollectible: true)
            {
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                return null; // 我们不需要从AssemblyName加载，仅从流加载
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
    }
}