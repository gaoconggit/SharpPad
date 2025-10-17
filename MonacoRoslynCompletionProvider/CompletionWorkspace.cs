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
        private static readonly string[] DefaultAssemblyPaths = BuildDefaultAssemblyPaths();

        private static string[] BuildDefaultAssemblyPaths()
        {
            var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trustedAssemblyMap = CreateTrustedAssemblyMap();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                TryAddAssembly(referencePaths, assembly);
            }

            TryAddType(typeof(object));
            TryAddType(typeof(Console));
            TryAddType(typeof(List<>));
            TryAddType(typeof(Dictionary<,>));
            TryAddType(typeof(IEnumerable<>));
            TryAddType(typeof(Enumerable));
            TryAddType(typeof(System.ComponentModel.DescriptionAttribute));
            TryAddType(typeof(System.ComponentModel.INotifyPropertyChanged));
            TryAddType(typeof(System.Linq.Expressions.Expression));
            TryAddType(typeof(System.Data.DataSet));
            TryAddType(typeof(System.Xml.XmlDocument));
            TryAddType(typeof(System.ComponentModel.DataAnnotations.ValidationException));
            TryAddType(typeof(Newtonsoft.Json.JsonConvert));
            TryAddType(typeof(Microsoft.Extensions.Primitives.StringValues));
            TryAddType(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute));
            TryAddType(typeof(Microsoft.AspNetCore.Http.HttpRequest));
            TryAddType(typeof(Microsoft.AspNetCore.Http.IHeaderDictionary));
            TryAddType(typeof(System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler));
            TryAddType(typeof(Microsoft.IdentityModel.Tokens.SecurityTokenHandler));
            TryAddType(typeof(System.Text.RegularExpressions.Regex));
            TryAddType(typeof(System.Text.Json.JsonSerializer));
            TryAddType(typeof(System.Net.Http.HttpClient));
            TryAddType(typeof(System.Net.Http.IHttpClientFactory));
            TryAddType(typeof(System.Buffers.ArrayPool<byte>));
            TryAddType(typeof(System.Collections.ArrayList));
            TryAddType(typeof(System.Net.WebHeaderCollection));
            TryAddType(typeof(System.Net.WebProxy));
            TryAddType(typeof(System.Security.Cryptography.HashAlgorithm));
            TryAddType(typeof(System.Net.Mail.MailAddress));
            TryAddType(typeof(Microsoft.Net.Http.Headers.MediaTypeHeaderValue));
            TryAddType(typeof(ObjectExtension));
            TryAddType(typeof(System.Diagnostics.Process));
            TryAddType(typeof(ParallelEnumerable));
            TryAddType(typeof(Uri));

            TryAddOptionalType("System.Net.HttpListener, System.Net.HttpListener");
            TryAddOptionalType("System.Windows.Forms.Form, System.Windows.Forms");
            TryAddOptionalType("System.Drawing.Image, System.Drawing");
            TryAddOptionalType("Microsoft.Win32.SystemEvents, Microsoft.Win32.SystemEvents");

            foreach (var assemblyName in new[]
            {
                "System.Runtime",
                "netstandard",
                "System.ComponentModel",
                "System.Collections",
                "System.Text.RegularExpressions",
                "Microsoft.Extensions.Primitives",
                "Microsoft.AspNetCore.Mvc.Abstractions",
                "Microsoft.AspNetCore.DataProtection",
                "Microsoft.AspNetCore.Http.Features",
                "System.Memory",
                "System.Private.Uri",
                "System.Net.Primitives",
                "System.Net.HttpListener",
                "System.Net.WebHeaderCollection",
                "System.Net.WebProxy",
                "Microsoft.Net.Http.Headers",
                "System.Security.Cryptography",
                "System.Net.Mail"
            })
            {
                TryAddByAssemblyName(assemblyName);
            }

            return referencePaths.OrderBy(path => path).ToArray();

            static Dictionary<string, string> CreateTrustedAssemblyMap()
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var raw = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return result;
                }

                foreach (var candidate in raw.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(candidate);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    result.TryAdd(name, candidate);
                }

                return result;
            }

            static void TryAddAssembly(HashSet<string> target, Assembly? assembly)
            {
                if (assembly is null || assembly.IsDynamic)
                {
                    return;
                }

                try
                {
                    var location = assembly.Location;
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        target.Add(location);
                    }
                }
                catch
                {
                    // assemblies loaded from memory do not have a file path
                }
            }

            void TryAddType(Type? type)
            {
                if (type is null)
                {
                    return;
                }

                TryAddAssembly(referencePaths, type.Assembly);
            }

            void TryAddOptionalType(string qualifiedTypeName)
            {
                if (string.IsNullOrWhiteSpace(qualifiedTypeName))
                {
                    return;
                }

                var type = Type.GetType(qualifiedTypeName, throwOnError: false);
                TryAddType(type);
            }

            void TryAddByAssemblyName(string assemblyName)
            {
                if (string.IsNullOrWhiteSpace(assemblyName))
                {
                    return;
                }

                if (trustedAssemblyMap.TryGetValue(assemblyName, out var path) && !string.IsNullOrWhiteSpace(path))
                {
                    referencePaths.Add(path);
                    return;
                }

                try
                {
                    var loaded = Assembly.Load(new AssemblyName(assemblyName));
                    TryAddAssembly(referencePaths, loaded);
                }
                catch
                {
                    // ignore load failures
                }
            }
        }

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
                    XmlDocumentationProvider documentationProvider = null;
                    
                    // 查找 XML 文档文件
                    var xmlDocPaths = FindXmlDocumentationFiles(path);
                    
                    if (xmlDocPaths.Count > 0)
                    {
                        var assemblyFileName = Path.GetFileNameWithoutExtension(path);
                        
                        // 对于 System.Private.CoreLib，使用专用的全量文档提供者
                        if (assemblyFileName == "System.Private.CoreLib")
                        {
                            documentationProvider = CoreLibXmlDocumentationProvider.Instance;
                        }
                        else if (xmlDocPaths.Count == 1)
                        {
                            documentationProvider = XmlDocumentationProvider.CreateFromFile(xmlDocPaths[0]);
                        }
                        else
                        {
                            // 对于多个文档文件，使用组合文档提供者
                            documentationProvider = new CompositeXmlDocumentationProvider(xmlDocPaths);
                        }
                    }
                    
                    return MetadataReference.CreateFromFile(path, documentation: documentationProvider);
                }
                catch
                {
                    // 可在此处记录加载失败的日志
                    return null;
                }
            });
        }
        
        // 查找 XML 文档文件的多个可能位置
        private static List<string> FindXmlDocumentationFiles(string assemblyPath)
        {
            var results = new List<string>();
            var assemblyFileName = Path.GetFileNameWithoutExtension(assemblyPath);
            var xmlFileName = assemblyFileName + ".xml";
            
            // 1. 同目录下查找
            var sameDirectoryXml = Path.Combine(Path.GetDirectoryName(assemblyPath), xmlFileName);
            if (File.Exists(sameDirectoryXml))
            {
                results.Add(sameDirectoryXml);
                return results;
            }
            
            // 2. 对于 .NET Core 程序集，查找参考程序集目录
            if (assemblyPath.Contains("Microsoft.NETCore.App") || assemblyPath.Contains("System.") || assemblyFileName == "System.Private.CoreLib")
            {
                // 检测当前 .NET 版本
                var dotnetVersion = Environment.Version;
                var majorVersion = dotnetVersion.Major;
                
                // 构建参考程序集路径
                var dotnetPacksPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "packs", "Microsoft.NETCore.App.Ref");
                
                if (Directory.Exists(dotnetPacksPath))
                {
                    // 查找对应版本的参考程序集
                    var versions = Directory.GetDirectories(dotnetPacksPath)
                        .Select(d => Path.GetFileName(d))
                        .Where(v => v.StartsWith($"{majorVersion}."))
                        .OrderByDescending(v => v)
                        .ToList();
                    
                    foreach (var version in versions)
                    {
                        var refPath = Path.Combine(dotnetPacksPath, version, "ref", $"net{majorVersion}.0");
                        
                        // 首先尝试直接匹配文件名
                        var refXmlPath = Path.Combine(refPath, xmlFileName);
                        if (File.Exists(refXmlPath))
                        {
                            results.Add(refXmlPath);
                            return results;
                        }
                        
                        // 对于 System.Private.CoreLib，使用终极解决方案：加载所有 XML 文档文件
                        if (assemblyFileName == "System.Private.CoreLib")
                        {
                            // 加载所有可用的 XML 文档文件以确保最大覆盖率
                            var xmlFiles = Directory.GetFiles(refPath, "*.xml", SearchOption.TopDirectoryOnly);
                            results.AddRange(xmlFiles.Where(File.Exists));
                            
                            if (results.Count > 0)
                                return results;
                        }
                    }
                }
            }
            
            // 3. 对于第三方库，尝试 NuGet 包缓存位置
            var nugetCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            if (Directory.Exists(nugetCachePath))
            {
                var packageDirectories = Directory.GetDirectories(nugetCachePath, assemblyFileName.ToLower() + "*", SearchOption.TopDirectoryOnly);
                foreach (var packageDir in packageDirectories.Take(1)) // 只检查第一个匹配的包
                {
                    var xmlFiles = Directory.GetFiles(packageDir, xmlFileName, SearchOption.AllDirectories);
                    if (xmlFiles.Length > 0)
                    {
                        results.Add(xmlFiles[0]);
                        return results;
                    }
                }
            }
            
            return results;
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

        private static void DisposeReferenceCacheEntries()
        {
            foreach (var reference in ReferenceCache.Values)
            {
                if (reference is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal issues; cache will be rebuilt as needed.
                    }
                }
            }
        }

        /// <summary>
        /// 清理 MetadataReference 缓存，用于重新加载包含文档的引用
        /// </summary>
        public static void ClearReferenceCache()
        {
            DisposeReferenceCacheEntries();
            ReferenceCache.Clear();
        }
        
        /// <summary>
        /// 测试方法：检查特定程序集的 XML 文档是否可以加载
        /// </summary>
        public static string TestXmlDocumentationLoading(string assemblyName = "System.Console")
        {
            try
            {
                Assembly assembly;
                if (assemblyName == "System.Private.CoreLib")
                {
                    assembly = typeof(List<>).Assembly;
                }
                else
                {
                    assembly = Assembly.Load(assemblyName);
                }
                
                var xmlDocPaths = FindXmlDocumentationFiles(assembly.Location);
                
                if (xmlDocPaths.Count == 0)
                    return $"未找到 {assemblyName} 的 XML 文档文件";
                
                var nonExistentFiles = xmlDocPaths.Where(path => !File.Exists(path)).ToList();
                if (nonExistentFiles.Count > 0)
                    return $"XML 文档文件不存在: {string.Join(", ", nonExistentFiles)}";
                
                if (assemblyName == "System.Private.CoreLib")
                {
                    var coreLibProvider = CoreLibXmlDocumentationProvider.Instance;
                    return $"成功加载 System.Private.CoreLib 全量文档提供者 (已加载 {coreLibProvider.LoadedDocumentCount} 个 XML 文档)";
                }
                else if (xmlDocPaths.Count == 1)
                {
                    var docProvider = XmlDocumentationProvider.CreateFromFile(xmlDocPaths[0]);
                    return $"成功加载 XML 文档: {xmlDocPaths[0]}";
                }
                else
                {
                    var compositeProvider = new CompositeXmlDocumentationProvider(xmlDocPaths);
                    var fileNames = xmlDocPaths.Select(p => Path.GetFileName(p)).Take(5).ToArray();
                    var displayNames = string.Join(", ", fileNames);
                    if (xmlDocPaths.Count > 5)
                        displayNames += $" 等{xmlDocPaths.Count}个文件";
                    return $"成功加载组合 XML 文档 ({xmlDocPaths.Count} 个): {displayNames}";
                }
            }
            catch (Exception ex)
            {
                return $"加载 XML 文档时出错: {ex.Message}";
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
            ClearReferenceCache();
        }
    }
}
