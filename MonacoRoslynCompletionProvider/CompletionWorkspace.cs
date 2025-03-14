using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
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
using MonacoRoslynCompletionProvider.Api;

namespace MonacoRoslynCompletionProvider
{
    /// <summary>
    /// 高性能的 CompletionWorkspace，复用工作空间和程序集引用缓存，
    /// 并通过合理的并发控制达到优化性能的目的。
    /// </summary>
    public sealed class CompletionWorkspace : IDisposable
    {
        // 缓存程序集引用，避免重复加载（线程安全）
        private static readonly ConcurrentDictionary<string, MetadataReference> ReferenceCache = new ConcurrentDictionary<string, MetadataReference>();

        // 默认程序集路径，仅加载必要的程序集
        private static readonly string[] DefaultAssemblyPaths = new[]
        {
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
            typeof(ObjectExtengsion).Assembly.Location, // 假设此类型存在
            typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly.Location,
            typeof(System.Diagnostics.Process).Assembly.Location
        };

        // 用于复用 AdhocWorkspace 减少创建开销
        private static readonly ConcurrentBag<AdhocWorkspace> WorkspacePool = new ConcurrentBag<AdhocWorkspace>();

        // 并发控制，数量可根据实际硬件调整
        private static readonly SemaphoreSlim ConcurrencySemaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);

        // MEF host services，Roslyn 工作空间依赖（单例）
        private static readonly Lazy<MefHostServices> HostServices = new Lazy<MefHostServices>(() =>
        {
            var assemblies = new[]
            {
                Assembly.Load("Microsoft.CodeAnalysis.Workspaces"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"),
                Assembly.Load("Microsoft.CodeAnalysis.Features"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")
            };
            return MefHostServices.Create(assemblies);
        });

        private AdhocWorkspace _workspace;
        private Project _project;
        // 存储所有程序集路径，初始化时已包含默认程序集
        private readonly HashSet<string> _metadataReferencePaths = new HashSet<string>(DefaultAssemblyPaths);
        private bool _disposed;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private CompletionWorkspace() { }

        /// <summary>
        /// 异步创建 CompletionWorkspace 实例，可以传入额外的程序集路径
        /// </summary>
        public static async Task<CompletionWorkspace> CreateAsync(params string[] additionalAssemblyPaths)
        {
            var instance = new CompletionWorkspace();
            await instance.InitializeAsync(additionalAssemblyPaths).ConfigureAwait(false);
            return instance;
        }

        private async Task InitializeAsync(string[] additionalAssemblyPaths)
        {
            // 添加额外的程序集引用（如果有）
            if (additionalAssemblyPaths != null)
            {
                foreach (var path in additionalAssemblyPaths)
                {
                    _metadataReferencePaths.Add(path);
                }
            }

            // 尝试复用工作空间，减少创建成本
            if (!WorkspacePool.TryTake(out _workspace))
            {
                _workspace = new AdhocWorkspace(HostServices.Value);
            }

            // 创建包含最基本引用的项目，保证启动速度
            var initialReferences = GetCoreReferences();
            var projectInfo = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "TempProject",
                "TempProject",
                LanguageNames.CSharp,
                metadataReferences: initialReferences
            );

            _project = _workspace.AddProject(projectInfo);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        // 返回最基本的程序集引用，保证项目能快速启动
        private List<MetadataReference> GetCoreReferences()
        {
            var essentialPaths = new[]
            {
                typeof(object).Assembly.Location,
                typeof(Console).Assembly.Location,
                Assembly.Load("System.Runtime").Location
            };

            return essentialPaths.Select(GetOrCreateMetadataReference)
                                 .Where(r => r != null)
                                 .ToList();
        }

        // 获取所有需要的程序集引用，利用缓存减少加载开销
        private List<MetadataReference> GetAllReferences()
        {
            return _metadataReferencePaths
                .Select(GetOrCreateMetadataReference)
                .Where(r => r != null)
                .ToList();
        }

        // 线程安全地获取或创建 MetadataReference
        private static MetadataReference GetOrCreateMetadataReference(string assemblyPath)
        {
            return ReferenceCache.GetOrAdd(assemblyPath, path =>
            {
                try
                {
                    return MetadataReference.CreateFromFile(path);
                }
                catch
                {
                    // 可在此处记录加载失败的日志
                    return null;
                }
            });
        }

        /// <summary>
        /// 创建用于代码补全的 CompletionDocument，
        /// 内部通过并发控制、复用工作空间和引用缓存达到高性能。
        /// </summary>
        public async Task<CompletionDocument> CreateDocumentAsync(
            string code,
            OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
            CancellationToken cancellationToken = default)
        {
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken))
            {
                var token = linkedCts.Token;
                await ConcurrencySemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    // 生成唯一的文档名称
                    string documentName = $"Document.cs";
                    var document = _workspace.AddDocument(_project.Id, documentName, SourceText.From(code));

                    // 更新项目，添加所有必要的程序集引用
                    var allReferences = GetAllReferences();
                    _project = document.Project.WithMetadataReferences(allReferences);
                    document = _project.GetDocument(document.Id);

                    // 异步获取语法树
                    var syntaxTree = await document.GetSyntaxTreeAsync(token).ConfigureAwait(false);

                    // 创建编译对象，启用并发构建和 Release 优化
                    var compilation = CSharpCompilation.Create(
                        "TempCompilation",
                        new[] { syntaxTree },
                        references: allReferences,
                        options: new CSharpCompilationOptions(outputKind)
                                    .WithOptimizationLevel(OptimizationLevel.Release)
                                    .WithConcurrentBuild(true)
                    );

                    // 获取语义模型（忽略可访问性检查，加快分析速度）
                    var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);

                    // 编译检查错误，写入内存流（仅用于诊断，不生成文件）
                    using (var ms = new MemoryStream())
                    {
                        var emitResult = compilation.Emit(ms, cancellationToken: token);
                        return new CompletionDocument(document, semanticModel, emitResult);
                    }
                }
                finally
                {
                    ConcurrencySemaphore.Release();
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cts.Cancel();
                _cts.Dispose();

                // 清理当前工作空间中的所有项目，保证状态干净
                var solution = _workspace.CurrentSolution;
                foreach (var projId in solution.ProjectIds)
                {
                    solution = solution.RemoveProject(projId);
                }
                _workspace.TryApplyChanges(solution);
                // 将工作空间返还到池中，以供后续复用
                WorkspacePool.Add(_workspace);
                _disposed = true;
            }
        }

        /// <summary>
        /// 应用退出时调用，释放所有工作空间及缓存资源。
        /// </summary>
        public static void Shutdown()
        {
            while (WorkspacePool.TryTake(out var workspace))
            {
                workspace.Dispose();
            }
            ReferenceCache.Clear();
        }
    }
}
