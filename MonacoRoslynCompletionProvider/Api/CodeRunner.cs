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

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeRunner
    {
        // 用于同步Console重定向的锁对象
        private static readonly object _consoleLock = new object();

        // 用于创建AssemblyLoadContext的锁对象
        private static readonly object _loadContextLock = new object();

        public class RunResult
        {
            public string Output { get; set; }
            public string Error { get; set; }
        }

        public static async Task<RunResult> RunProgramCodeAsync(string code, string nuget, int languageVersion, Func<string, Task> onOutput,
            Func<string, Task> onError)
        {
            var result = new RunResult();
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            // 创建一个可卸载的AssemblyLoadContext
            var loadContext = new CustomAssemblyLoadContext();

            try
            {
                // 加载 NuGet 包
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);

                // 设置解析选项，包括语言版本
                var parseOptions = new CSharpParseOptions(
                    languageVersion: (LanguageVersion)languageVersion,
                    kind: SourceCodeKind.Regular,
                    documentationMode: DocumentationMode.Parse
                );

                // 生成唯一的程序集名称，避免并发冲突
                string uniqueAssemblyName = $"DynamicCode";

                // 解析代码
                var syntaxTree = CSharpSyntaxTree.ParseText(code, parseOptions);
                var defaultReferences = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .Select(a => MetadataReference.CreateFromFile(a.Location));

                // 添加 NuGet 包引用
                var allReferences = defaultReferences
                    .Concat(nugetAssemblies.Select(assembly =>
                        MetadataReference.CreateFromFile(assembly.Location)));

                var compilation = CSharpCompilation.Create(
                    assemblyName: uniqueAssemblyName,
                    syntaxTrees: new[] { syntaxTree },
                    references: allReferences,
                    options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

                using var peStream = new MemoryStream();
                {
                    var compileResult = compilation.Emit(peStream);

                    if (!compileResult.Success)
                    {
                        var errors = string.Join(Environment.NewLine, compileResult.Diagnostics
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => d.ToString()));
                        await SafeInvokeCallback(onError, $"Compilation error:\n{errors}");
                        result.Error = errors;
                        return result;
                    }

                    peStream.Seek(0, SeekOrigin.Begin);

                    // 使用自定义LoadContext加载程序集
                    Assembly assembly;
                    lock (_loadContextLock)
                    {
                        assembly = loadContext.LoadFromStream(peStream);
                    }

                    var entryPoint = assembly.EntryPoint;

                    if (entryPoint != null)
                    {
                        var parameters = entryPoint.GetParameters();

                        // 使用本地变量来存储当前线程的控制台输出
                        TextWriter threadLocalOutput = null;
                        TextWriter threadLocalError = null;

                        await using var outputWriter = new CallbackTextWriter(
                            async text => await SafeInvokeCallback(onOutput, text),
                            outputBuilder);
                        await using var errorWriter = new CallbackTextWriter(async text => await SafeInvokeCallback(onError, text),
                            errorBuilder);

                        // Execute the entry point asynchronously to prevent blocking
                        var executionTask = Task.Run(async () =>
                        {
                            try
                            {
                                // 使用锁保护Console重定向
                                lock (_consoleLock)
                                {
                                    threadLocalOutput = Console.Out;
                                    threadLocalError = Console.Error;
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
                                finally
                                {
                                    // 确保在同一个锁内恢复控制台输出
                                    lock (_consoleLock)
                                    {
                                        if (threadLocalOutput != null)
                                            Console.SetOut(threadLocalOutput);
                                        if (threadLocalError != null)
                                            Console.SetError(threadLocalError);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                string errorMessage = $"Execution error: {ex.InnerException?.InnerException?.Message ?? ex.InnerException?.Message ?? ex.Message}";
                                await SafeInvokeCallback(onError, errorMessage);
                                errorBuilder.AppendLine(errorMessage);
                            }
                        });

                        await executionTask;
                    }
                    else
                    {
                        await SafeInvokeCallback(onError, "No entry point found in the code.");
                        errorBuilder.AppendLine("No entry point found in the code.");
                    }
                }
            }
            catch (Exception ex)
            {
                await SafeInvokeCallback(onError, $"Runtime error: {ex.Message}");
                errorBuilder.AppendLine($"Runtime error: {ex.Message}");
            }
            finally
            {
                // 执行完毕后卸载程序集
                loadContext.Unload();

                // 强制GC回收内存
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            result.Output = outputBuilder.ToString();
            result.Error = errorBuilder.ToString();
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

        // 线程安全的回调执行方法
        private static async Task SafeInvokeCallback(Func<string, Task> callback, string message)
        {
            if (callback != null)
            {
                try
                {
                    await callback(message);
                }
                catch
                {
                    // 忽略回调中的异常 
                }
            }
        }

        // 辅助类用于实时回调输出并累加输出内容
        private class CallbackTextWriter : TextWriter
        {
            private readonly Action<string> _writeAction;
            private readonly StringBuilder _builder;

            public CallbackTextWriter(Action<string> writeAction, StringBuilder builder)
            {
                _writeAction = writeAction;
                _builder = builder;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                var str = value.ToString();
                _builder.Append(str);
                _writeAction?.Invoke(str);
            }

            public override void Write(string value)
            {
                _builder.Append(value);
                _writeAction?.Invoke(value);
            }

            public override void WriteLine(string value)
            {
                var str = value + Environment.NewLine;
                _builder.Append(str);
                _writeAction?.Invoke(str);
            }

            public override Task WriteAsync(char value)
            {
                var str = value.ToString();
                _builder.Append(str);
                _writeAction?.Invoke(str);
                return Task.CompletedTask;
            }

            public override Task WriteAsync(string value)
            {
                _builder.Append(value);
                _writeAction?.Invoke(value);
                return Task.CompletedTask;
            }

            public override Task WriteLineAsync(string value)
            {
                var str = value + Environment.NewLine;
                _builder.Append(str);
                _writeAction?.Invoke(str);
                return Task.CompletedTask;
            }
        }

        public static void DownloadPackage(string nuget)
        {
            DownloadNugetPackages.DownloadAllPackages(nuget);
        }
    }
}