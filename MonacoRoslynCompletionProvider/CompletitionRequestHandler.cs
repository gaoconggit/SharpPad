﻿using System.Composition.Hosting;
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
using monacoEditorCSharp.DataHelpers;
using System.IO;

namespace MonacoRoslynCompletionProvider
{
    public static class CompletitionRequestHandler
    {
        private static readonly string[] s_assemblies = [
            .. AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location).ToArray(),
            Path.Combine("Dll", "System.Text.Json.dll"),
            Path.Combine("Dll", "FreeSql.dll"),
            Path.Combine("Dll", "CSRedisCore.dll"),
            Path.Combine("Dll", "RestSharp.dll"),
        ];

        public async static Task<TabCompletionResult[]> Handle(TabCompletionRequest tabCompletionRequest, string nuget)
        {
            // 加载 NuGet 包
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Location).ToArray();
            var workspace = CompletionWorkspace.Create([.. nugetAssembliesArray, .. s_assemblies]);
            var document = await workspace.CreateDocument(tabCompletionRequest.Code);
            return await document.GetTabCompletion(tabCompletionRequest.Position, CancellationToken.None);
        }

        public async static Task<HoverInfoResult> Handle(HoverInfoRequest hoverInfoRequest, string nuget)
        {
            // 加载 NuGet 包
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Location).ToArray();

            var workspace = CompletionWorkspace.Create([.. nugetAssembliesArray, .. s_assemblies]);
            var document = await workspace.CreateDocument(hoverInfoRequest.Code);
            return await document.GetHoverInformation(hoverInfoRequest.Position, CancellationToken.None);
        }

        public async static Task<CodeCheckResult[]> Handle(CodeCheckRequest codeCheckRequest, string nuget)
        {
            // 加载 NuGet 包
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Location).ToArray();

            var workspace = CompletionWorkspace.Create([.. nugetAssembliesArray, .. s_assemblies]);
            var document = await workspace.CreateDocument(codeCheckRequest.Code);
            return await document.GetCodeCheckResults(CancellationToken.None);
        }

        public async static Task<SignatureHelpResult> Handle(SignatureHelpRequest signatureHelpRequest, string nuget)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Location).ToArray();
            var workspace = CompletionWorkspace.Create([.. nugetAssembliesArray, .. s_assemblies]);
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
