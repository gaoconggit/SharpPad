using System.Composition.Hosting;
using System.Linq;
using MonacoRoslynCompletionProvider.Api;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using System;
using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider
{
    public static class CompletitionRequestHandler
    {
        private static readonly string[] s_assemblies = [
            ".\\Dll\\System.Text.Json.dll",
            ".\\Dll\\FreeSql.dll",
            ".\\Dll\\CSRedisCore.dll",
            ".\\Dll\\RestSharp.dll",
        ];
        public async static Task<TabCompletionResult[]> Handle(TabCompletionRequest tabCompletionRequest)
        {
            var workspace = CompletionWorkspace.Create(tabCompletionRequest.Assemblies = s_assemblies);
            var document = await workspace.CreateDocument(tabCompletionRequest.Code);
            return await document.GetTabCompletion(tabCompletionRequest.Position, CancellationToken.None);
        }

        public async static Task<HoverInfoResult> Handle(HoverInfoRequest hoverInfoRequest)
        {
            var workspace = CompletionWorkspace.Create(hoverInfoRequest.Assemblies = s_assemblies);
            var document = await workspace.CreateDocument(hoverInfoRequest.Code);
            return await document.GetHoverInformation(hoverInfoRequest.Position, CancellationToken.None);
        }

        public async static Task<CodeCheckResult[]> Handle(CodeCheckRequest codeCheckRequest)
        {
            var workspace = CompletionWorkspace.Create(codeCheckRequest.Assemblies = s_assemblies);
            var document = await workspace.CreateDocument(codeCheckRequest.Code);
            return await document.GetCodeCheckResults(CancellationToken.None);
        }

        public async static Task<SignatureHelpResult> Handle(SignatureHelpRequest signatureHelpRequest)
        {
            var workspace = CompletionWorkspace.Create(signatureHelpRequest.Assemblies = s_assemblies);
            var document = await workspace.CreateDocument(signatureHelpRequest.Code);
            return await document.GetSignatureHelp(signatureHelpRequest.Position, CancellationToken.None);
        }

        //格式化代码
        public static string FormatCode(string sourceCode)
        {

            var assemblies = new[]
            {
                Assembly.Load("Microsoft.CodeAnalysis"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp"),
                Assembly.Load("Microsoft.CodeAnalysis.Features"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"),
            };

            var partTypes = MefHostServices.DefaultAssemblies.Concat(assemblies)
                .Distinct()
                .SelectMany(x => x.GetTypes())
                .ToArray();

            var compositionContext = new ContainerConfiguration()
                .WithParts(partTypes)
                .CreateContainer();

            var host = MefHostServices.Create(compositionContext);

            var workspace = new AdhocWorkspace(host);
            var sourceLanguage = new CSharpLanguage();

            SyntaxTree syntaxTree = sourceLanguage.ParseText(sourceCode, SourceCodeKind.Script);
            var root = (CompilationUnitSyntax)syntaxTree.GetRoot();
            return Formatter.Format(root, workspace).ToFullString();
        }

        public class CSharpLanguage : ILanguageService
        {
            private static readonly LanguageVersion MaxLanguageVersion = Enum
                .GetValues(typeof(LanguageVersion))
                .Cast<LanguageVersion>()
                .Max();

            public SyntaxTree ParseText(string sourceCode, SourceCodeKind kind)
            {
                var options = new CSharpParseOptions(kind: kind, languageVersion: MaxLanguageVersion);

                // Return a syntax tree of our source code
                return CSharpSyntaxTree.ParseText(sourceCode, options);
            }

            public Compilation CreateLibraryCompilation(string assemblyName, bool enableOptimisations)
            {
                throw new NotImplementedException();
            }
        }
    }
}
