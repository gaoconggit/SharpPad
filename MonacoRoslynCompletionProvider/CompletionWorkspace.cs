using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;

namespace MonacoRoslynCompletionProvider
{
    public class CompletionWorkspace
    {
        public static MetadataReference[] DefaultMetadataReferences = new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(int).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
                MetadataReference.CreateFromFile(typeof(DescriptionAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DataSet).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(XmlDocument).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(INotifyPropertyChanged).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Http.HttpRequest).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Http.IHeaderDictionary).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(JwtSecurityTokenHandler).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(SecurityTokenHandler).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("Microsoft.Extensions.Primitives").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.ComponentModel").Location),
                MetadataReference.CreateFromFile(Assembly.Load("Microsoft.AspNetCore.Mvc.Abstractions").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Text.RegularExpressions").Location),
                MetadataReference.CreateFromFile(Assembly.Load("Microsoft.AspNetCore.DataProtection").Location),
                MetadataReference.CreateFromFile(Assembly.Load("Newtonsoft.Json").Location),
                MetadataReference.CreateFromFile(typeof(ValidationException).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Net.Http, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(typeof(IHttpClientFactory).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Memory, Version=8.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Private.Uri, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Net.Primitives, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("Microsoft.Net.Http.Headers, Version=8.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Security.Cryptography, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("Microsoft.AspNetCore.Http").Location),
                MetadataReference.CreateFromFile(typeof(ObjectExtengsion).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly.Location)
            };

        private Project _project;
        private AdhocWorkspace _workspace;
        private List<MetadataReference> _metadataReferences;

        public static CompletionWorkspace Create(params string[] assemblies)
        {
            Assembly[] lst = new[] {
                Assembly.Load("Microsoft.CodeAnalysis.Workspaces"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"),
                Assembly.Load("Microsoft.CodeAnalysis.Features"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")
            };

            var host = MefHostServices.Create(lst);
            var workspace = new AdhocWorkspace(host);

            var references = DefaultMetadataReferences.ToList();

            if (assemblies != null && assemblies.Length > 0)
            {
                for (int i = 0; i < assemblies.Length; i++)
                {
                    references.Add(MetadataReference.CreateFromFile(assemblies[i]));
                }
            }

            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "TempProject", "TempProject", LanguageNames.CSharp)
                .WithMetadataReferences(references);
            var project = workspace.AddProject(projectInfo);


            return new CompletionWorkspace() { _workspace = workspace, _project = project, _metadataReferences = references };
        }

        public async Task<CompletionDocument> CreateDocument(string code, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
        {
            var document = _workspace.AddDocument(_project.Id, "MyFile2.cs", SourceText.From(code));
            var st = await document.GetSyntaxTreeAsync();
            var compilation =
            CSharpCompilation
                .Create("Temp",
                    new[] { st },
                    options: new CSharpCompilationOptions(outputKind),
                    references: _metadataReferences
                );

            using (var temp = new MemoryStream())
            {
                var result = compilation.Emit(temp);
                var semanticModel = compilation.GetSemanticModel(st, true);

                return new CompletionDocument(document, semanticModel, result);
            }
        }
    }
}
