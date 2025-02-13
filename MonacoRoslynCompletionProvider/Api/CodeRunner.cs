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

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeRunner
    {
        public class RunResult
        {
            public string Output { get; set; }
            public string Error { get; set; }
        }

        // Start of Selection
        public static async Task<RunResult> RunProgramCodeAsync(string code, string nuget, Action<string> onOutput, Action<string> onError)
        {
            var result = new RunResult();
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                // 加载 NuGet 包
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);

                // 设置解析选项，包括语言版本
                //var parseOptions = new CSharpParseOptions(
                //    languageVersion: LanguageVersion.CSharp13,
                //    kind: SourceCodeKind.Regular,
                //    documentationMode: DocumentationMode.Parse
                //);

                // 解析代码
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var defaultReferences = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .Select(a => MetadataReference.CreateFromFile(a.Location));

                // 添加 NuGet 包引用
                var allReferences = defaultReferences
                    .Concat(nugetAssemblies.Select(assembly =>
                        MetadataReference.CreateFromFile(assembly.Location)));

                var compilation = CSharpCompilation.Create(
                    assemblyName: "DynamicCode",
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
                        onError?.Invoke($"Compilation error:\n{errors}");
                        result.Error = errors;
                        return result;
                    }

                    peStream.Seek(0, SeekOrigin.Begin);
                    var assembly = Assembly.Load(peStream.ToArray());
                    var entryPoint = assembly.EntryPoint;

                    if (entryPoint != null)
                    {
                        var parameters = entryPoint.GetParameters();

                        var originalOutput = Console.Out;
                        var originalError = Console.Error;

                        await using var outputWriter = new CallbackTextWriter(onOutput, outputBuilder);
                        await using var errorWriter = new CallbackTextWriter(onError, errorBuilder);
                        Console.SetOut(outputWriter);
                        Console.SetError(errorWriter);

                        // Execute the entry point asynchronously to prevent blocking
                        var executionTask = Task.Run(() =>
                        {
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
                                onError?.Invoke($"Execution error: {ex.InnerException?.InnerException?.Message
                                                                    ?? ex.InnerException?.Message
                                                                    ?? ex.Message}");
                                errorBuilder.AppendLine($"Execution error: {ex.InnerException?.InnerException?.Message
                                                                            ?? ex.InnerException?.Message
                                                                            ?? ex.Message}");
                            }
                        });

                        await executionTask;

                        // 恢复控制台输出
                        Console.SetOut(originalOutput);
                        Console.SetError(originalError);
                    }
                    else
                    {
                        onError?.Invoke("No entry point found in the code.");
                        errorBuilder.AppendLine("No entry point found in the code.");
                    }
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Runtime error: {ex.Message}");
                errorBuilder.AppendLine($"Runtime error: {ex.Message}");
            }

            result.Output = outputBuilder.ToString();
            result.Error = errorBuilder.ToString();
            return result;
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
