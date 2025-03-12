using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider
{
    public class CompletionWorkspace : IDisposable
    {
        // Static cache for metadata references
        private static readonly ConcurrentDictionary<string, MetadataReference> _referenceCache = new ConcurrentDictionary<string, MetadataReference>();

        // Static list of default assembly names to be loaded
        private static readonly string[] _defaultAssemblyNames = [
            typeof(Console).Assembly.Location,
            Assembly.Load("System.Runtime").Location,
            typeof(List<>).Assembly.Location,
            typeof(int).Assembly.Location,
            Assembly.Load("netstandard").Location,
            typeof(System.ComponentModel.DescriptionAttribute).Assembly.Location,
            typeof(object).Assembly.Location,
            typeof(Dictionary<,>).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(System.Data.DataSet).Assembly.Location,
            typeof(System.Xml.XmlDocument).Assembly.Location,
            typeof(System.ComponentModel.INotifyPropertyChanged).Assembly.Location,
            typeof(System.Linq.Expressions.Expression).Assembly.Location,
            typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute).Assembly.Location,
            typeof(Microsoft.AspNetCore.Http.HttpRequest).Assembly.Location,
            typeof(Microsoft.AspNetCore.Http.IHeaderDictionary).Assembly.Location,
            typeof(System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler).Assembly.Location,
            typeof(Microsoft.IdentityModel.Tokens.SecurityTokenHandler).Assembly.Location,
            Assembly.Load("Microsoft.Extensions.Primitives").Location,
            Assembly.Load("System.ComponentModel").Location,
            Assembly.Load("Microsoft.AspNetCore.Mvc.Abstractions").Location,
            Assembly.Load("System.Collections").Location,
            Assembly.Load("System.Text.RegularExpressions").Location,
            Assembly.Load("Microsoft.AspNetCore.DataProtection").Location,
            Assembly.Load("Newtonsoft.Json").Location,
            typeof(System.ComponentModel.DataAnnotations.ValidationException).Assembly.Location,
            Assembly.Load("System.Net.Http, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location,
            typeof(System.Net.Http.IHttpClientFactory).Assembly.Location,
            Assembly.Load("System.Memory, Version=8.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51").Location,
            Assembly.Load("System.Private.Uri, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location,
            Assembly.Load("System.Net.Primitives, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location,
            Assembly.Load("Microsoft.Net.Http.Headers, Version=8.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60").Location,
            Assembly.Load("System.Security.Cryptography, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location,
            Assembly.Load("Microsoft.AspNetCore.Http").Location,
            typeof(ObjectExtengsion).Assembly.Location,
            typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly.Location,
            typeof(System.Diagnostics.Process).Assembly.Location
        ];

        // Workspace object pool
        private static readonly ConcurrentBag<AdhocWorkspace> _workspacePool = new ConcurrentBag<AdhocWorkspace>();

        // Semaphore to limit concurrent operations
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

        // MEF host services (created once and shared)
        private static readonly Lazy<MefHostServices> _host = new Lazy<MefHostServices>(() =>
        {
            Assembly[] assemblies = new[] {
                Assembly.Load("Microsoft.CodeAnalysis.Workspaces"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"),
                Assembly.Load("Microsoft.CodeAnalysis.Features"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")
            };
            return MefHostServices.Create(assemblies);
        });

        private AdhocWorkspace _workspace;
        private Project _project;
        private bool _disposed = false;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        // Instead of storing references directly, store assembly names for lazy loading
        private readonly HashSet<string> _metadataReferenceNames = new HashSet<string>();

        private CompletionWorkspace() { }

        public static async Task<CompletionWorkspace> CreateAsync(params string[] additionalAssemblies)
        {
            var workspace = new CompletionWorkspace();
            await workspace.InitializeAsync(additionalAssemblies);
            return workspace;
        }

        private async Task InitializeAsync(string[] additionalAssemblies)
        {
            await Task.Run(() =>
            {
                // Add default assembly names
                foreach (var assembly in _defaultAssemblyNames)
                {
                    _metadataReferenceNames.Add(assembly);
                }

                // Add additional assembly names
                if (additionalAssemblies != null)
                {
                    foreach (var assembly in additionalAssemblies)
                    {
                        _metadataReferenceNames.Add(assembly);
                    }
                }

                // Try to get a workspace from the pool or create a new one
                if (!_workspacePool.TryTake(out _workspace))
                {
                    _workspace = new AdhocWorkspace(_host.Value);
                }

                // Create project with minimal references initially
                var initialReferences = GetCoreReferences();
                var projectInfo = ProjectInfo.Create(
                    ProjectId.CreateNewId(),
                    VersionStamp.Create(),
                    "TempProject",
                    "TempProject",
                    LanguageNames.CSharp)
                    .WithMetadataReferences(initialReferences);

                _project = _workspace.AddProject(projectInfo);
            });
        }

        // Gets only essential references for initial project creation
        private List<MetadataReference> GetCoreReferences()
        {
            var essentialAssemblies = new[]
            {
                typeof(object).Assembly.Location,
                typeof(Console).Assembly.Location,
                Assembly.Load("System.Runtime").Location
            };

            return essentialAssemblies.Select(GetOrCreateMetadataReference).ToList();
        }

        // Lazy-loads the full set of references
        private List<MetadataReference> GetAllReferences()
        {
            return _metadataReferenceNames
                .AsParallel()
                .Select(GetOrCreateMetadataReference)
                .ToList();
        }

        // Gets or creates a metadata reference (thread-safe)
        private static MetadataReference GetOrCreateMetadataReference(string assemblyPath)
        {
            return _referenceCache.GetOrAdd(assemblyPath, path =>
            {
                try
                {
                    return MetadataReference.CreateFromFile(path);
                }
                catch (Exception)
                {
                    Console.WriteLine($"Failed to load assembly: {path}");
                    return null;
                }
            });
        }

        public async Task<CompletionDocument> CreateDocumentAsync(string code, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary, CancellationToken cancellationToken = default)
        {
            // Combine with internal cancellation token
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken))
            {
                var linkedToken = linkedCts.Token;

                // Wait for a slot in the semaphore
                await _semaphore.WaitAsync(linkedToken);

                try
                {
                    // Create a unique document ID to avoid conflicts in concurrent scenarios
                    string documentName = $"Document_{Guid.NewGuid()}.cs";

                    // Create document with source text
                    var document = _workspace.AddDocument(_project.Id, documentName, SourceText.From(code));

                    // Ensure we have all references needed
                    var allReferences = GetAllReferences();
                    var updatedProject = document.Project.WithMetadataReferences(allReferences);
                    document = updatedProject.GetDocument(document.Id);

                    // Get syntax tree and create compilation in parallel
                    var syntaxTreeTask = document.GetSyntaxTreeAsync(linkedToken);

                    // Process in parallel where possible
                    var st = await syntaxTreeTask;

                    // Create compilation with the updated references
                    var compilation = CSharpCompilation.Create(
                        "Temp",
                        new[] { st },
                        options: new CSharpCompilationOptions(outputKind)
                            .WithOptimizationLevel(OptimizationLevel.Release)
                            .WithConcurrentBuild(true),
                        references: allReferences.Where(r => r != null)
                    );

                    // Get semantic model
                    var semanticModel = compilation.GetSemanticModel(st, true);

                    // Emit to check for errors
                    using (var stream = new MemoryStream())
                    {
                        var emitResult = compilation.Emit(stream, cancellationToken: linkedToken);

                        // Create completion document
                        return new CompletionDocument(document, semanticModel, emitResult);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cancel any ongoing operations
                    _cts.Cancel();
                    _cts.Dispose();

                    // Clear the workspace by creating a new solution without any projects
                    var solution = _workspace.CurrentSolution;
                    foreach (var projectId in solution.ProjectIds)
                    {
                        solution = solution.RemoveProject(projectId);
                    }

                    // Apply the empty solution
                    _workspace.TryApplyChanges(solution);

                    // Return workspace to the pool instead of disposing it
                    _workspacePool.Add(_workspace);
                }
                _disposed = true;
            }
        }

        // Cleanup method to call during application shutdown
        public static void Shutdown()
        {
            // Dispose all pooled workspaces
            while (_workspacePool.TryTake(out var workspace))
            {
                workspace.Dispose();
            }

            // Clear reference cache
            _referenceCache.Clear();
        }
    }
}