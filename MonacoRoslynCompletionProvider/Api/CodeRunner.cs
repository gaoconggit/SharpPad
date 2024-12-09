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

        public static RunResult RunProgramCode(string code, string nuget)
        {

            var result = new RunResult();
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                //下载 nuget 包
                //DownloadNugetPackages.DownloadAllPackages(nuget);
                var nugetAssemblies = DownloadNugetPackages.LoadPackages(nuget);

                // 重定向控制台输出
                var originalOutput = Console.Out;
                var originalError = Console.Error;
                using (var outputWriter = new StringWriter(outputBuilder))
                using (var errorWriter = new StringWriter(errorBuilder))
                {
                    Console.SetOut(outputWriter);
                    Console.SetError(errorWriter);

                    // 原有的编译和运行代码
                    var syntaxTree = CSharpSyntaxTree.ParseText(code);
                    var defaultReferences = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                        .Select(a => MetadataReference.CreateFromFile(a.Location))
                        .Cast<MetadataReference>();

                    // 将 NuGet 包的引用添加到引用集合中
                    var allReferences = defaultReferences
                        .Concat(nugetAssemblies.Select(assembly =>
                            MetadataReference.CreateFromFile(assembly.Location)))
                        .Cast<MetadataReference>();

                    var compilation = CSharpCompilation.Create(
                        assemblyName: "DynamicCode",
                        syntaxTrees: new[] { syntaxTree },
                        references: allReferences,
                        options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

                    using (var peStream = new MemoryStream())
                    {
                        var compileResult = compilation.Emit(peStream);

                        if (!compileResult.Success)
                        {
                            var errors = string.Join(Environment.NewLine, compileResult.Diagnostics
                                .Where(d => d.Severity == DiagnosticSeverity.Error)
                                .Select(d => d.ToString()));
                            errorBuilder.Append($"Compilation error:\n{errors}");
                        }
                        else
                        {
                            peStream.Seek(0, SeekOrigin.Begin);
                            var assembly = Assembly.Load(peStream.ToArray());
                            var entryPoint = assembly.EntryPoint;

                            if (entryPoint != null)
                            {
                                entryPoint.Invoke(null, null);
                            }
                            else
                            {
                                errorBuilder.Append("No entry point found in the code.");
                            }
                        }
                    }

                    // 恢复控制台输出
                    Console.SetOut(originalOutput);
                    Console.SetError(originalError);
                }
            }
            catch (Exception ex)
            {
                errorBuilder.Append($"Runtime error: {ex.Message}");
            }

            result.Output = outputBuilder.ToString();
            result.Error = errorBuilder.ToString();
            return result;
        }

        public static void DownloadPackage(string nuget)
        {
            DownloadNugetPackages.DownloadAllPackages(nuget);
        }
    }
}
