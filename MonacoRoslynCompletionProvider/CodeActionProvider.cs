using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonacoRoslynCompletionProvider.Api;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider
{
    internal class CodeActionProvider
    {
        public async Task<CodeActionResult[]> ProvideAsync(Document document, int position, int selectionStart, int selectionEnd, CancellationToken cancellationToken = default)
        {
            var results = new List<CodeActionResult>();
            
            try
            {
                // 获取语义模型和语法树
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var sourceText = await document.GetTextAsync(cancellationToken);
                
                if (syntaxRoot == null || semanticModel == null)
                    return results.ToArray();

                // 创建文本范围
                var span = new TextSpan(selectionStart, selectionEnd - selectionStart);
                if (span.Length == 0)
                {
                    // 如果没有选择范围，使用位置处的单个字符
                    span = new TextSpan(position, 0);
                }

                // 获取诊断信息
                var diagnostics = semanticModel.GetDiagnostics(span, cancellationToken);
                var syntaxDiagnostics = syntaxRoot.GetDiagnostics();
                
                // 合并所有相关的诊断
                var allDiagnostics = diagnostics
                    .Concat(syntaxDiagnostics.Where(d => span.IntersectsWith(d.Location.SourceSpan)))
                    .Where(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning)
                    .Distinct()
                    .ToArray();

                // 为每个诊断提供 Code Actions
                foreach (var diagnostic in allDiagnostics)
                {
                    var codeActions = await GetCodeActionsForDiagnostic(document, diagnostic, cancellationToken);
                    results.AddRange(codeActions);
                }

                // 添加通用的 Code Actions（如添加 using 语句等）
                var genericActions = await GetGenericCodeActions(document, span, cancellationToken);
                results.AddRange(genericActions);
            }
            catch (System.Exception ex)
            {
                // 记录错误但不抛出异常，返回空结果
                System.Diagnostics.Debug.WriteLine($"Code Action Provider Error: {ex.Message}");
            }

            return results.ToArray();
        }

        private async Task<List<CodeActionResult>> GetCodeActionsForDiagnostic(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var results = new List<CodeActionResult>();

            try
            {
                // 根据诊断类型提供相应的修复建议
                switch (diagnostic.Id)
                {
                    case "CS0103": // 未找到类型或命名空间
                        results.AddRange(await CreateAddUsingActions(document, diagnostic, cancellationToken));
                        break;
                    case "CS0246": // 找不到类型或命名空间名称
                        results.AddRange(await CreateAddUsingActions(document, diagnostic, cancellationToken));
                        break;
                    case "CS1002": // 应输入 ;
                        results.Add(CreateAddSemicolonAction(document, diagnostic));
                        break;
                    case "CS0161": // 并非所有代码路径都返回值
                        results.Add(CreateAddReturnStatementAction(document, diagnostic));
                        break;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating code action for diagnostic {diagnostic.Id}: {ex.Message}");
            }

            return results;
        }

        private async Task<List<CodeActionResult>> CreateAddUsingActions(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var results = new List<CodeActionResult>();
            
            try
            {
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var sourceText = await document.GetTextAsync(cancellationToken);
                
                if (syntaxRoot == null || sourceText == null)
                    return results;

                // 获取错误位置的标识符
                var diagnosticSpan = diagnostic.Location.SourceSpan;
                var token = syntaxRoot.FindToken(diagnosticSpan.Start);
                var identifier = token.ValueText;

                // 常见的命名空间映射
                var commonNamespaces = new Dictionary<string, string[]>
                {
                    // System基础类型
                    ["Console"] = new[] { "System" },
                    ["DateTime"] = new[] { "System" },
                    ["TimeSpan"] = new[] { "System" },
                    ["Guid"] = new[] { "System" },
                    ["Uri"] = new[] { "System" },
                    ["Random"] = new[] { "System" },
                    ["Environment"] = new[] { "System" },
                    ["Convert"] = new[] { "System" },
                    ["Math"] = new[] { "System" },
                    ["Array"] = new[] { "System" },
                    ["Tuple"] = new[] { "System" },
                    ["BitConverter"] = new[] { "System" },
                    ["Version"] = new[] { "System" },
                    ["Exception"] = new[] { "System" },
                    ["EventArgs"] = new[] { "System" },
                    ["EventHandler"] = new[] { "System" },
                    ["Action"] = new[] { "System" },
                    ["Func"] = new[] { "System" },
                    ["Predicate"] = new[] { "System" },
                    ["Lazy"] = new[] { "System" },
                    ["WeakReference"] = new[] { "System" },

                    // 集合类型
                    ["List"] = new[] { "System.Collections.Generic" },
                    ["Dictionary"] = new[] { "System.Collections.Generic" },
                    ["HashSet"] = new[] { "System.Collections.Generic" },
                    ["Queue"] = new[] { "System.Collections.Generic" },
                    ["Stack"] = new[] { "System.Collections.Generic" },
                    ["LinkedList"] = new[] { "System.Collections.Generic" },
                    ["SortedSet"] = new[] { "System.Collections.Generic" },
                    ["SortedDictionary"] = new[] { "System.Collections.Generic" },
                    ["SortedList"] = new[] { "System.Collections.Generic" },
                    ["KeyValuePair"] = new[] { "System.Collections.Generic" },
                    ["IEnumerable"] = new[] { "System.Collections.Generic" },
                    ["ICollection"] = new[] { "System.Collections.Generic" },
                    ["IList"] = new[] { "System.Collections.Generic" },
                    ["IDictionary"] = new[] { "System.Collections.Generic" },
                    ["ISet"] = new[] { "System.Collections.Generic" },
                    ["ConcurrentBag"] = new[] { "System.Collections.Concurrent" },
                    ["ConcurrentQueue"] = new[] { "System.Collections.Concurrent" },
                    ["ConcurrentStack"] = new[] { "System.Collections.Concurrent" },
                    ["ConcurrentDictionary"] = new[] { "System.Collections.Concurrent" },
                    ["BlockingCollection"] = new[] { "System.Collections.Concurrent" },

                    // 文本处理
                    ["StringBuilder"] = new[] { "System.Text" },
                    ["Encoding"] = new[] { "System.Text" },
                    ["Regex"] = new[] { "System.Text.RegularExpressions" },
                    ["Match"] = new[] { "System.Text.RegularExpressions" },
                    ["JsonSerializer"] = new[] { "System.Text.Json" },
                    ["JsonDocument"] = new[] { "System.Text.Json" },
                    ["JsonElement"] = new[] { "System.Text.Json" },

                    // I/O操作
                    ["File"] = new[] { "System.IO" },
                    ["Path"] = new[] { "System.IO" },
                    ["Directory"] = new[] { "System.IO" },
                    ["Stream"] = new[] { "System.IO" },
                    ["FileStream"] = new[] { "System.IO" },
                    ["MemoryStream"] = new[] { "System.IO" },
                    ["StreamReader"] = new[] { "System.IO" },
                    ["StreamWriter"] = new[] { "System.IO" },
                    ["BinaryReader"] = new[] { "System.IO" },
                    ["BinaryWriter"] = new[] { "System.IO" },
                    ["FileInfo"] = new[] { "System.IO" },
                    ["DirectoryInfo"] = new[] { "System.IO" },
                    ["DriveInfo"] = new[] { "System.IO" },
                    ["FileSystemWatcher"] = new[] { "System.IO" },
                    ["StringReader"] = new[] { "System.IO" },
                    ["StringWriter"] = new[] { "System.IO" },
                    ["TextReader"] = new[] { "System.IO" },
                    ["TextWriter"] = new[] { "System.IO" },
                    ["ZipArchive"] = new[] { "System.IO.Compression" },
                    ["ZipFile"] = new[] { "System.IO.Compression" },
                    ["GZipStream"] = new[] { "System.IO.Compression" },

                    // 网络相关
                    ["HttpClient"] = new[] { "System.Net.Http" },
                    ["HttpRequestMessage"] = new[] { "System.Net.Http" },
                    ["HttpResponseMessage"] = new[] { "System.Net.Http" },
                    ["HttpContent"] = new[] { "System.Net.Http" },
                    ["StringContent"] = new[] { "System.Net.Http" },
                    ["WebClient"] = new[] { "System.Net" },
                    ["IPAddress"] = new[] { "System.Net" },
                    ["IPEndPoint"] = new[] { "System.Net" },
                    ["Dns"] = new[] { "System.Net" },
                    ["Cookie"] = new[] { "System.Net" },
                    ["TcpClient"] = new[] { "System.Net.Sockets" },
                    ["TcpListener"] = new[] { "System.Net.Sockets" },
                    ["UdpClient"] = new[] { "System.Net.Sockets" },
                    ["Socket"] = new[] { "System.Net.Sockets" },

                    // 多线程
                    ["Task"] = new[] { "System.Threading.Tasks" },
                    ["Thread"] = new[] { "System.Threading" },
                    ["CancellationToken"] = new[] { "System.Threading" },
                    ["CancellationTokenSource"] = new[] { "System.Threading" },
                    ["Semaphore"] = new[] { "System.Threading" },
                    ["SemaphoreSlim"] = new[] { "System.Threading" },
                    ["Mutex"] = new[] { "System.Threading" },
                    ["Monitor"] = new[] { "System.Threading" },
                    ["Timer"] = new[] { "System.Threading" },
                    ["ThreadPool"] = new[] { "System.Threading" },
                    ["Interlocked"] = new[] { "System.Threading" },
                    ["ReaderWriterLock"] = new[] { "System.Threading" },
                    ["ReaderWriterLockSlim"] = new[] { "System.Threading" },
                    ["AutoResetEvent"] = new[] { "System.Threading" },
                    ["ManualResetEvent"] = new[] { "System.Threading" },
                    ["Barrier"] = new[] { "System.Threading" },
                    ["CountdownEvent"] = new[] { "System.Threading" },

                    // XML处理
                    ["XmlDocument"] = new[] { "System.Xml" },
                    ["XmlReader"] = new[] { "System.Xml" },
                    ["XmlWriter"] = new[] { "System.Xml" },
                    ["XmlNode"] = new[] { "System.Xml" },
                    ["XmlElement"] = new[] { "System.Xml" },
                    ["XDocument"] = new[] { "System.Xml.Linq" },
                    ["XElement"] = new[] { "System.Xml.Linq" },
                    ["XAttribute"] = new[] { "System.Xml.Linq" },
                    ["XName"] = new[] { "System.Xml.Linq" },
                    ["XmlSerializer"] = new[] { "System.Xml.Serialization" },

                    // 数据相关
                    ["DataTable"] = new[] { "System.Data" },
                    ["DataSet"] = new[] { "System.Data" },
                    ["DataRow"] = new[] { "System.Data" },
                    ["DataColumn"] = new[] { "System.Data" },
                    ["DataView"] = new[] { "System.Data" },
                    ["SqlConnection"] = new[] { "System.Data.SqlClient" },
                    ["SqlCommand"] = new[] { "System.Data.SqlClient" },
                    ["SqlDataReader"] = new[] { "System.Data.SqlClient" },
                    ["SqlDataAdapter"] = new[] { "System.Data.SqlClient" },

                    // 诊断和调试
                    ["Debug"] = new[] { "System.Diagnostics" },
                    ["Trace"] = new[] { "System.Diagnostics" },
                    ["Stopwatch"] = new[] { "System.Diagnostics" },
                    ["Process"] = new[] { "System.Diagnostics" },
                    ["StackTrace"] = new[] { "System.Diagnostics" },

                    // 安全相关
                    ["SHA256"] = new[] { "System.Security.Cryptography" },
                    ["MD5"] = new[] { "System.Security.Cryptography" },
                    ["AES"] = new[] { "System.Security.Cryptography" },
                    ["RSA"] = new[] { "System.Security.Cryptography" },
                    ["HMACSHA256"] = new[] { "System.Security.Cryptography" },
                    ["RandomNumberGenerator"] = new[] { "System.Security.Cryptography" },

                    // 反射
                    ["Assembly"] = new[] { "System.Reflection" },
                    ["Type"] = new[] { "System" },
                    ["MethodInfo"] = new[] { "System.Reflection" },
                    ["PropertyInfo"] = new[] { "System.Reflection" },
                    ["FieldInfo"] = new[] { "System.Reflection" },

                    // LINQ扩展方法
                    ["AsParallel"] = new[] { "System.Linq" },
                    ["Where"] = new[] { "System.Linq" },
                    ["Select"] = new[] { "System.Linq" },
                    ["SelectMany"] = new[] { "System.Linq" },
                    ["OrderBy"] = new[] { "System.Linq" },
                    ["OrderByDescending"] = new[] { "System.Linq" },
                    ["ThenBy"] = new[] { "System.Linq" },
                    ["ThenByDescending"] = new[] { "System.Linq" },
                    ["GroupBy"] = new[] { "System.Linq" },
                    ["Join"] = new[] { "System.Linq" },
                    ["GroupJoin"] = new[] { "System.Linq" },
                    ["First"] = new[] { "System.Linq" },
                    ["FirstOrDefault"] = new[] { "System.Linq" },
                    ["Last"] = new[] { "System.Linq" },
                    ["LastOrDefault"] = new[] { "System.Linq" },
                    ["Single"] = new[] { "System.Linq" },
                    ["SingleOrDefault"] = new[] { "System.Linq" },
                    ["Any"] = new[] { "System.Linq" },
                    ["All"] = new[] { "System.Linq" },
                    ["Count"] = new[] { "System.Linq" },
                    ["LongCount"] = new[] { "System.Linq" },
                    ["Sum"] = new[] { "System.Linq" },
                    ["Average"] = new[] { "System.Linq" },
                    ["Min"] = new[] { "System.Linq" },
                    ["Max"] = new[] { "System.Linq" },
                    ["ToArray"] = new[] { "System.Linq" },
                    ["ToList"] = new[] { "System.Linq" },
                    ["ToDictionary"] = new[] { "System.Linq" },
                    ["ToHashSet"] = new[] { "System.Linq" },
                    ["Take"] = new[] { "System.Linq" },
                    ["TakeLast"] = new[] { "System.Linq" },
                    ["TakeWhile"] = new[] { "System.Linq" },
                    ["Skip"] = new[] { "System.Linq" },
                    ["SkipLast"] = new[] { "System.Linq" },
                    ["SkipWhile"] = new[] { "System.Linq" },
                    ["Distinct"] = new[] { "System.Linq" },
                    ["DistinctBy"] = new[] { "System.Linq" },
                    ["Reverse"] = new[] { "System.Linq" },
                    ["Concat"] = new[] { "System.Linq" },
                    ["Union"] = new[] { "System.Linq" },
                    ["Intersect"] = new[] { "System.Linq" },
                    ["Except"] = new[] { "System.Linq" },
                    ["Zip"] = new[] { "System.Linq" },
                    ["Aggregate"] = new[] { "System.Linq" },
                    ["Contains"] = new[] { "System.Linq" },
                    ["SequenceEqual"] = new[] { "System.Linq" },
                    ["DefaultIfEmpty"] = new[] { "System.Linq" },
                    ["ElementAt"] = new[] { "System.Linq" },
                    ["ElementAtOrDefault"] = new[] { "System.Linq" },
                    ["Chunk"] = new[] { "System.Linq" }
                };

                if (commonNamespaces.ContainsKey(identifier))
                {
                    foreach (var namespaceName in commonNamespaces[identifier])
                    {
                        var action = CreateAddUsingAction(document, namespaceName, sourceText, syntaxRoot);
                        if (action != null)
                            results.Add(action);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating add using actions: {ex.Message}");
            }

            return results;
        }

        private CodeActionResult? CreateAddUsingAction(Document document, string namespaceName, SourceText sourceText, SyntaxNode syntaxRoot)
        {
            try
            {
                // 检查是否已存在该 using 语句
                var existingUsings = syntaxRoot.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                    .Select(u => u.Name?.ToString())
                    .ToHashSet();

                if (existingUsings.Contains(namespaceName))
                    return null;

                // 找到插入 using 语句的位置
                var insertPosition = 0;
                var lines = sourceText.Lines;
                
                // 在现有 using 语句之后插入，或在文件开头
                foreach (var line in lines)
                {
                    var lineText = line.ToString().Trim();
                    if (lineText.StartsWith("using ") || lineText.StartsWith("namespace ") || 
                        lineText.StartsWith("class ") || lineText.StartsWith("public "))
                    {
                        if (lineText.StartsWith("using "))
                            insertPosition = line.End;
                        else
                            break;
                    }
                    else if (!string.IsNullOrWhiteSpace(lineText))
                    {
                        break;
                    }
                }

                var insertLine = sourceText.Lines.GetLineFromPosition(insertPosition);
                
                return new CodeActionResult
                {
                    Title = $"Add using {namespaceName};",
                    Kind = "quickfix",
                    IsPreferred = true,
                    ActionId = $"add-using-{namespaceName}",
                    Edits = new List<CodeEdit>
                    {
                        new CodeEdit
                        {
                            StartLine = insertLine.LineNumber + 1,
                            StartColumn = 1,
                            EndLine = insertLine.LineNumber + 1,
                            EndColumn = 1,
                            NewText = $"using {namespaceName};\n"
                        }
                    }
                };
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating add using action: {ex.Message}");
                return null;
            }
        }

        private CodeActionResult CreateAddSemicolonAction(Document document, Diagnostic diagnostic)
        {
            var span = diagnostic.Location.SourceSpan;
            var line = diagnostic.Location.GetLineSpan().StartLinePosition;
            
            return new CodeActionResult
            {
                Title = "Add semicolon",
                Kind = "quickfix",
                IsPreferred = true,
                ActionId = "add-semicolon",
                Edits = new List<CodeEdit>
                {
                    new CodeEdit
                    {
                        StartLine = line.Line + 1,
                        StartColumn = line.Character + 1,
                        EndLine = line.Line + 1,
                        EndColumn = line.Character + 1,
                        NewText = ";"
                    }
                }
            };
        }

        private CodeActionResult CreateAddReturnStatementAction(Document document, Diagnostic diagnostic)
        {
            var span = diagnostic.Location.SourceSpan;
            var line = diagnostic.Location.GetLineSpan().EndLinePosition;
            
            return new CodeActionResult
            {
                Title = "Add return statement",
                Kind = "quickfix",
                IsPreferred = true,
                ActionId = "add-return",
                Edits = new List<CodeEdit>
                {
                    new CodeEdit
                    {
                        StartLine = line.Line,
                        StartColumn = line.Character,
                        EndLine = line.Line,
                        EndColumn = line.Character,
                        NewText = "\n    return default;"
                    }
                }
            };
        }

        private async Task<List<CodeActionResult>> GetGenericCodeActions(Document document, TextSpan span, CancellationToken cancellationToken)
        {
            var results = new List<CodeActionResult>();
            
            try
            {
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var sourceText = await document.GetTextAsync(cancellationToken);
                
                if (syntaxRoot != null && sourceText != null)
                {
                    // 组织 using 语句
                    var usingDirectives = syntaxRoot.DescendantNodes().OfType<UsingDirectiveSyntax>().ToList();
                    if (usingDirectives.Count > 1)
                    {
                        results.Add(CreateOrganizeUsingsAction());
                    }

                    // 检查可能缺失的常用using语句
                    var existingUsings = usingDirectives
                        .Select(u => u.Name?.ToString())
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToHashSet();

                    // 扫描代码中的标识符，检查是否需要添加using语句
                    var tokens = syntaxRoot.DescendantTokens()
                        .Where(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken))
                        .Where(t => span.IntersectsWith(t.Span) || span.Length == 0)
                        .Select(t => t.ValueText)
                        .Distinct()
                        .ToArray();

                    // 常见的方法到命名空间映射
                    var commonUsings = new Dictionary<string, string[]>
                    {
                        ["AsParallel"] = new[] { "System.Linq" },
                        ["Where"] = new[] { "System.Linq" },
                        ["Select"] = new[] { "System.Linq" },
                        ["First"] = new[] { "System.Linq" },
                        ["Any"] = new[] { "System.Linq" },
                        ["ToList"] = new[] { "System.Linq" },
                        ["ToArray"] = new[] { "System.Linq" }
                    };

                    foreach (var token in tokens)
                    {
                        if (commonUsings.ContainsKey(token))
                        {
                            foreach (var namespaceName in commonUsings[token])
                            {
                                if (!existingUsings.Contains(namespaceName))
                                {
                                    var action = CreateAddUsingAction(document, namespaceName, sourceText, syntaxRoot);
                                    if (action != null && !results.Any(r => r.Title == action.Title))
                                    {
                                        results.Add(action);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating generic code actions: {ex.Message}");
            }

            return results;
        }

        private CodeActionResult CreateOrganizeUsingsAction()
        {
            return new CodeActionResult
            {
                Title = "Organize usings",
                Kind = "source.organizeImports",
                IsPreferred = false,
                ActionId = "organize-usings",
                Edits = new List<CodeEdit>(),
                Data = new { action = "organize-usings" }
            };
        }
    }
}