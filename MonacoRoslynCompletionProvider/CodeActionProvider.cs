using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonacoRoslynCompletionProvider.Api;
using System;
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
                    case "CS1061": // 未找到扩展方法（缺少 using 指令）
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

                // 对于扩展方法调用 (CS1061)，尝试从诊断消息或语法节点中提取方法名
                if (diagnostic.Id == "CS1061")
                {
                    var node = syntaxRoot.FindNode(diagnosticSpan);
                    if (node is MemberAccessExpressionSyntax memberAccess)
                    {
                        identifier = memberAccess.Name.Identifier.ValueText;
                    }
                    else if (node is IdentifierNameSyntax identifierName)
                    {
                        identifier = identifierName.Identifier.ValueText;
                    }
                    else
                    {
                        // 从诊断消息中提取方法名: "未包含"Dump"的定义"
                        var message = diagnostic.GetMessage();
                        var match = System.Text.RegularExpressions.Regex.Match(message, @"""(\w+)""");
                        if (match.Success)
                        {
                            identifier = match.Groups[1].Value;
                        }
                    }
                }
                else
                {
                    // 对于其他错误类型，检查是否是方法调用
                    var node = syntaxRoot.FindNode(diagnosticSpan);
                    if (node is InvocationExpressionSyntax invocation)
                    {
                        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                        {
                            identifier = memberAccess.Name.Identifier.ValueText;
                        }
                        else if (invocation.Expression is IdentifierNameSyntax identifierName)
                        {
                            identifier = identifierName.Identifier.ValueText;
                        }
                    }
                }

                // 优先从当前编译中找可用的命名空间（与 completion 使用同一数据源）
                var suggestedNamespaces = await FindNamespacesFromCompilationAsync(
                    document,
                    identifier,
                    cancellationToken);

                var suggestedSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var namespaceName in suggestedNamespaces)
                {
                    if (!suggestedSet.Add(namespaceName))
                    {
                        continue;
                    }

                    var action = CreateAddUsingAction(document, namespaceName, sourceText, syntaxRoot);
                    if (action != null)
                    {
                        results.Add(action);
                    }
                }

                // 常见的命名空间映射（兜底）
                var commonNamespaces = new Dictionary<string, string[]>
                {
                    // System基础类型
                    ["Console"] = new[] { "System" },
                    ["ArrayBufferWriter"] = new[] { "System.Buffers" },
                    ["ArrayPool"] = new[] { "System.Buffers" },
                    ["Dump"] = new[] { "System" },
                    ["Debugger"] = new[] { "System.Diagnostics" },
                    ["ToJson"] = new[] { "System" },
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
                    ["IDisposable"] = new[] { "System" },
                    ["IAsyncDisposable"] = new[] { "System" },
                    ["IComparable"] = new[] { "System" },
                    ["IEquatable"] = new[] { "System" },
                    ["IFormatProvider"] = new[] { "System" },
                    ["IFormattable"] = new[] { "System" },
                    ["ICloneable"] = new[] { "System" },
                    ["IObservable"] = new[] { "System" },
                    ["IObserver"] = new[] { "System" },
                    ["IProgress"] = new[] { "System" },
                    ["IServiceProvider"] = new[] { "System" },
                    ["GC"] = new[] { "System" },
                    ["Buffer"] = new[] { "System" },
                    ["AppDomain"] = new[] { "System" },
                    ["OperatingSystem"] = new[] { "System" },
                    ["Index"] = new[] { "System" },
                    ["Range"] = new[] { "System" },
                    ["HashCode"] = new[] { "System" },
                    ["Half"] = new[] { "System" },
                    ["DateOnly"] = new[] { "System" },
                    ["TimeOnly"] = new[] { "System" },
                    ["DateTimeOffset"] = new[] { "System" },
                    ["TimeZoneInfo"] = new[] { "System" },
                    ["Enum"] = new[] { "System" },
                    ["Delegate"] = new[] { "System" },
                    ["MulticastDelegate"] = new[] { "System" },
                    ["Nullable"] = new[] { "System" },
                    ["ValueTuple"] = new[] { "System" },
                    ["Memory"] = new[] { "System" },
                    ["Span"] = new[] { "System" },
                    ["ReadOnlySpan"] = new[] { "System" },
                    ["ReadOnlyMemory"] = new[] { "System" },
                    ["ArraySegment"] = new[] { "System" },

                    // System特性（Attributes）
                    ["STAThread"] = new[] { "System" },
                    ["MTAThread"] = new[] { "System" },
                    ["STAThreadAttribute"] = new[] { "System" },
                    ["MTAThreadAttribute"] = new[] { "System" },
                    ["Obsolete"] = new[] { "System" },
                    ["ObsoleteAttribute"] = new[] { "System" },
                    ["Serializable"] = new[] { "System" },
                    ["SerializableAttribute"] = new[] { "System" },
                    ["NonSerialized"] = new[] { "System" },
                    ["NonSerializedAttribute"] = new[] { "System" },
                    ["Flags"] = new[] { "System" },
                    ["FlagsAttribute"] = new[] { "System" },
                    ["CLSCompliant"] = new[] { "System" },
                    ["CLSCompliantAttribute"] = new[] { "System" },
                    ["AttributeUsage"] = new[] { "System" },
                    ["AttributeUsageAttribute"] = new[] { "System" },
                    ["ParamArray"] = new[] { "System" },
                    ["ParamArrayAttribute"] = new[] { "System" },
                    ["ThreadStatic"] = new[] { "System" },
                    ["ThreadStaticAttribute"] = new[] { "System" },
                    ["ContextStatic"] = new[] { "System" },
                    ["ContextStaticAttribute"] = new[] { "System" },
                    ["LoaderOptimization"] = new[] { "System" },
                    ["LoaderOptimizationAttribute"] = new[] { "System" },

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
                    ["ConstructorInfo"] = new[] { "System.Reflection" },
                    ["EventInfo"] = new[] { "System.Reflection" },
                    ["ParameterInfo"] = new[] { "System.Reflection" },
                    ["MemberInfo"] = new[] { "System.Reflection" },
                    ["BindingFlags"] = new[] { "System.Reflection" },

                    // ComponentModel（常用于属性验证和通知）
                    ["INotifyPropertyChanged"] = new[] { "System.ComponentModel" },
                    ["PropertyChangedEventArgs"] = new[] { "System.ComponentModel" },
                    ["PropertyChangedEventHandler"] = new[] { "System.ComponentModel" },
                    ["BindingList"] = new[] { "System.ComponentModel" },
                    ["TypeConverter"] = new[] { "System.ComponentModel" },
                    ["DefaultValue"] = new[] { "System.ComponentModel" },
                    ["DefaultValueAttribute"] = new[] { "System.ComponentModel" },
                    ["Description"] = new[] { "System.ComponentModel" },
                    ["DescriptionAttribute"] = new[] { "System.ComponentModel" },
                    ["DisplayName"] = new[] { "System.ComponentModel" },
                    ["DisplayNameAttribute"] = new[] { "System.ComponentModel" },
                    ["Category"] = new[] { "System.ComponentModel" },
                    ["CategoryAttribute"] = new[] { "System.ComponentModel" },
                    ["Browsable"] = new[] { "System.ComponentModel" },
                    ["BrowsableAttribute"] = new[] { "System.ComponentModel" },
                    ["ReadOnly"] = new[] { "System.ComponentModel" },
                    ["ReadOnlyAttribute"] = new[] { "System.ComponentModel" },
                    ["EditorBrowsable"] = new[] { "System.ComponentModel" },
                    ["EditorBrowsableAttribute"] = new[] { "System.ComponentModel" },
                    ["TypeDescriptor"] = new[] { "System.ComponentModel" },

                    // ComponentModel.DataAnnotations（验证特性）
                    ["Required"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["RequiredAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["StringLength"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["StringLengthAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["MaxLength"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["MaxLengthAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["MinLength"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["MinLengthAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["Range"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["RangeAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["RegularExpression"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["RegularExpressionAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["EmailAddress"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["EmailAddressAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["Phone"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["PhoneAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["Url"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["UrlAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["CreditCard"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["CreditCardAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["Compare"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["CompareAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["Display"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["DisplayAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["Key"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["KeyAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["DataType"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["DataTypeAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["ValidationContext"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["ValidationAttribute"] = new[] { "System.ComponentModel.DataAnnotations" },
                    ["Validator"] = new[] { "System.ComponentModel.DataAnnotations" },

                    // Runtime特性
                    ["CompilerGenerated"] = new[] { "System.Runtime.CompilerServices" },
                    ["CompilerGeneratedAttribute"] = new[] { "System.Runtime.CompilerServices" },
                    ["MethodImpl"] = new[] { "System.Runtime.CompilerServices" },
                    ["MethodImplAttribute"] = new[] { "System.Runtime.CompilerServices" },
                    ["CallerMemberName"] = new[] { "System.Runtime.CompilerServices" },
                    ["CallerMemberNameAttribute"] = new[] { "System.Runtime.CompilerServices" },
                    ["CallerFilePath"] = new[] { "System.Runtime.CompilerServices" },
                    ["CallerFilePathAttribute"] = new[] { "System.Runtime.CompilerServices" },
                    ["CallerLineNumber"] = new[] { "System.Runtime.CompilerServices" },
                    ["CallerLineNumberAttribute"] = new[] { "System.Runtime.CompilerServices" },
                    ["AsyncStateMachine"] = new[] { "System.Runtime.CompilerServices" },
                    ["AsyncStateMachineAttribute"] = new[] { "System.Runtime.CompilerServices" },
                    ["IteratorStateMachine"] = new[] { "System.Runtime.CompilerServices" },
                    ["IteratorStateMachineAttribute"] = new[] { "System.Runtime.CompilerServices" },
                    ["ConfiguredTaskAwaitable"] = new[] { "System.Runtime.CompilerServices" },
                    ["TaskAwaiter"] = new[] { "System.Runtime.CompilerServices" },

                    // Runtime.InteropServices（P/Invoke和COM互操作）
                    ["DllImport"] = new[] { "System.Runtime.InteropServices" },
                    ["DllImportAttribute"] = new[] { "System.Runtime.InteropServices" },
                    ["StructLayout"] = new[] { "System.Runtime.InteropServices" },
                    ["StructLayoutAttribute"] = new[] { "System.Runtime.InteropServices" },
                    ["MarshalAs"] = new[] { "System.Runtime.InteropServices" },
                    ["MarshalAsAttribute"] = new[] { "System.Runtime.InteropServices" },
                    ["FieldOffset"] = new[] { "System.Runtime.InteropServices" },
                    ["FieldOffsetAttribute"] = new[] { "System.Runtime.InteropServices" },
                    ["ComImport"] = new[] { "System.Runtime.InteropServices" },
                    ["ComImportAttribute"] = new[] { "System.Runtime.InteropServices" },
                    ["Guid"] = new[] { "System.Runtime.InteropServices", "System" },
                    ["GuidAttribute"] = new[] { "System.Runtime.InteropServices" },
                    ["IntPtr"] = new[] { "System" },
                    ["UIntPtr"] = new[] { "System" },
                    ["Marshal"] = new[] { "System.Runtime.InteropServices" },
                    ["SafeHandle"] = new[] { "System.Runtime.InteropServices" },

                    // Diagnostics特性
                    ["Conditional"] = new[] { "System.Diagnostics" },
                    ["ConditionalAttribute"] = new[] { "System.Diagnostics" },
                    ["DebuggerDisplay"] = new[] { "System.Diagnostics" },
                    ["DebuggerDisplayAttribute"] = new[] { "System.Diagnostics" },
                    ["DebuggerStepThrough"] = new[] { "System.Diagnostics" },
                    ["DebuggerStepThroughAttribute"] = new[] { "System.Diagnostics" },
                    ["DebuggerHidden"] = new[] { "System.Diagnostics" },
                    ["DebuggerHiddenAttribute"] = new[] { "System.Diagnostics" },
                    ["DebuggerBrowsable"] = new[] { "System.Diagnostics" },
                    ["DebuggerBrowsableAttribute"] = new[] { "System.Diagnostics" },
                    ["DebuggerNonUserCode"] = new[] { "System.Diagnostics" },
                    ["DebuggerNonUserCodeAttribute"] = new[] { "System.Diagnostics" },

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
                    ["Chunk"] = new[] { "System.Linq" },

                    // ASP.NET Core - MVC/API
                    ["Controller"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["ControllerBase"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["ApiController"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["Route"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["HttpGet"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["HttpPost"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["HttpPut"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["HttpDelete"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["HttpPatch"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["FromBody"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["FromQuery"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["FromRoute"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["FromHeader"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["FromForm"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["FromServices"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["IActionResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["ActionResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["ViewResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["JsonResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["OkResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["BadRequestResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["NotFoundResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["CreatedResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["NoContentResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["StatusCodeResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["FileResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["RedirectResult"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["ModelStateDictionary"] = new[] { "Microsoft.AspNetCore.Mvc.ModelBinding" },
                    ["BindProperty"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["Produces"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["Consumes"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["ProducesResponseType"] = new[] { "Microsoft.AspNetCore.Mvc" },

                    // ASP.NET Core - Middleware & Builder
                    ["WebApplication"] = new[] { "Microsoft.AspNetCore.Builder" },
                    ["WebApplicationBuilder"] = new[] { "Microsoft.AspNetCore.Builder" },
                    ["IApplicationBuilder"] = new[] { "Microsoft.AspNetCore.Builder" },
                    ["IEndpointRouteBuilder"] = new[] { "Microsoft.AspNetCore.Routing" },
                    ["IServiceCollection"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["IConfiguration"] = new[] { "Microsoft.Extensions.Configuration" },
                    ["IHostEnvironment"] = new[] { "Microsoft.Extensions.Hosting" },
                    ["IWebHostEnvironment"] = new[] { "Microsoft.AspNetCore.Hosting" },
                    ["ILogger"] = new[] { "Microsoft.Extensions.Logging" },
                    ["ILoggerFactory"] = new[] { "Microsoft.Extensions.Logging" },
                    ["IHostApplicationLifetime"] = new[] { "Microsoft.Extensions.Hosting" },
                    ["HostApplicationBuilder"] = new[] { "Microsoft.Extensions.Hosting" },
                    ["IHost"] = new[] { "Microsoft.Extensions.Hosting" },
                    ["IHostBuilder"] = new[] { "Microsoft.Extensions.Hosting" },
                    ["ConfigurationBuilder"] = new[] { "Microsoft.Extensions.Configuration" },
                    ["ConfigurationManager"] = new[] { "Microsoft.Extensions.Configuration" },

                    // ASP.NET Core - HTTP Context
                    ["HttpContext"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["HttpRequest"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["HttpResponse"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["IFormFile"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["IFormFileCollection"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["IHeaderDictionary"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["IQueryCollection"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["ISession"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["PathString"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["QueryString"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["StatusCodes"] = new[] { "Microsoft.AspNetCore.Http" },

                    // ASP.NET Core - Authentication & Authorization
                    ["Authorize"] = new[] { "Microsoft.AspNetCore.Authorization" },
                    ["AllowAnonymous"] = new[] { "Microsoft.AspNetCore.Authorization" },
                    ["IAuthorizationService"] = new[] { "Microsoft.AspNetCore.Authorization" },
                    ["AuthorizeAttribute"] = new[] { "Microsoft.AspNetCore.Authorization" },
                    ["ClaimsPrincipal"] = new[] { "System.Security.Claims" },
                    ["ClaimsIdentity"] = new[] { "System.Security.Claims" },
                    ["Claim"] = new[] { "System.Security.Claims" },
                    ["SignInManager"] = new[] { "Microsoft.AspNetCore.Identity" },
                    ["UserManager"] = new[] { "Microsoft.AspNetCore.Identity" },
                    ["RoleManager"] = new[] { "Microsoft.AspNetCore.Identity" },
                    ["IdentityUser"] = new[] { "Microsoft.AspNetCore.Identity" },
                    ["IdentityRole"] = new[] { "Microsoft.AspNetCore.Identity" },

                    // ASP.NET Core - SignalR
                    ["Hub"] = new[] { "Microsoft.AspNetCore.SignalR" },
                    ["IHubContext"] = new[] { "Microsoft.AspNetCore.SignalR" },
                    ["HubConnection"] = new[] { "Microsoft.AspNetCore.SignalR.Client" },
                    ["HubConnectionBuilder"] = new[] { "Microsoft.AspNetCore.SignalR.Client" },

                    // ASP.NET Core - Razor Pages
                    ["PageModel"] = new[] { "Microsoft.AspNetCore.Mvc.RazorPages" },
                    ["IPageModel"] = new[] { "Microsoft.AspNetCore.Mvc.RazorPages" },

                    // ASP.NET Core - Middleware Components
                    ["RequestDelegate"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["IMiddleware"] = new[] { "Microsoft.AspNetCore.Http" },
                    ["UseMiddleware"] = new[] { "Microsoft.AspNetCore.Builder" },
                    ["UseRouting"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing" },
                    ["UseEndpoints"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing" },
                    ["UseAuthorization"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Authorization" },
                    ["UseAuthentication"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Authentication" },
                    ["UseCors"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Cors" },
                    ["UseStaticFiles"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.StaticFiles" },
                    ["UseHttpsRedirection"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.HttpsPolicy" },
                    ["UseDeveloperExceptionPage"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Diagnostics" },
                    ["UseExceptionHandler"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Diagnostics" },
                    ["MapControllers"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing" },
                    ["MapGet"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing" },
                    ["MapPost"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing" },
                    ["MapPut"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing" },
                    ["MapDelete"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing" },
                    ["MapMethods"] = new[] { "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Routing" },
                    ["UseSwagger"] = new[] { "Microsoft.AspNetCore.Builder", "Swashbuckle.AspNetCore.Swagger" },
                    ["UseSwaggerUI"] = new[] { "Microsoft.AspNetCore.Builder", "Swashbuckle.AspNetCore.SwaggerUI" },

                    // Microsoft.Extensions.DependencyInjection - Core Types
                    ["IServiceProvider"] = new[] { "System", "Microsoft.Extensions.DependencyInjection" },
                    ["ServiceCollection"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["ServiceProvider"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["ServiceDescriptor"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["ServiceLifetime"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["IServiceScope"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["IServiceScopeFactory"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["ServiceProviderServiceExtensions"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["ActivatorUtilities"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["ObjectFactory"] = new[] { "Microsoft.Extensions.DependencyInjection" },

                    // ASP.NET Core - Dependency Injection Extensions
                    ["AddControllers"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.Mvc" },
                    ["AddControllersWithViews"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.Mvc" },
                    ["AddRazorPages"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.Mvc.RazorPages" },
                    ["AddEndpointsApiExplorer"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["AddSwaggerGen"] = new[] { "Microsoft.Extensions.DependencyInjection", "Swashbuckle.AspNetCore.SwaggerGen" },
                    ["AddDbContext"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.EntityFrameworkCore" },
                    ["AddScoped"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["AddSingleton"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["AddTransient"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["AddHostedService"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Hosting" },
                    ["AddHttpClient"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Http" },
                    ["AddLogging"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging" },
                    ["AddOptions"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Options" },
                    ["AddMemoryCache"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Caching.Memory" },
                    ["AddDistributedMemoryCache"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Caching.Distributed" },
                    ["AddSession"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.Session" },
                    ["AddCors"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.Cors" },
                    ["AddAuthentication"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.Authentication" },
                    ["AddAuthorization"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.Authorization" },
                    ["AddMvc"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.Mvc" },
                    ["AddSignalR"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.AspNetCore.SignalR" },
                    ["AddHealthChecks"] = new[] { "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Diagnostics.HealthChecks" },
                    ["Configure"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["BuildServiceProvider"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["GetService"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["GetRequiredService"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["GetServices"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["CreateScope"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["TryAdd"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["TryAddScoped"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["TryAddSingleton"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["TryAddTransient"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["TryAddEnumerable"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["Replace"] = new[] { "Microsoft.Extensions.DependencyInjection" },
                    ["RemoveAll"] = new[] { "Microsoft.Extensions.DependencyInjection" },

                    // ASP.NET Core - Options Pattern
                    ["IOptions"] = new[] { "Microsoft.Extensions.Options" },
                    ["IOptionsSnapshot"] = new[] { "Microsoft.Extensions.Options" },
                    ["IOptionsMonitor"] = new[] { "Microsoft.Extensions.Options" },
                    ["OptionsBuilder"] = new[] { "Microsoft.Extensions.Options" },
                    ["OptionsConfigurationServiceCollectionExtensions"] = new[] { "Microsoft.Extensions.DependencyInjection" },

                    // ASP.NET Core - Filters
                    ["IActionFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IAsyncActionFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IResultFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IAsyncResultFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IAuthorizationFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IAsyncAuthorizationFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IExceptionFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IAsyncExceptionFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IResourceFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["IAsyncResourceFilter"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["ActionFilterAttribute"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["ExceptionFilterAttribute"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["ResultFilterAttribute"] = new[] { "Microsoft.AspNetCore.Mvc.Filters" },
                    ["ServiceFilterAttribute"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["TypeFilterAttribute"] = new[] { "Microsoft.AspNetCore.Mvc" },

                    // ASP.NET Core - Model Binding & Validation
                    ["IModelBinder"] = new[] { "Microsoft.AspNetCore.Mvc.ModelBinding" },
                    ["IModelBinderProvider"] = new[] { "Microsoft.AspNetCore.Mvc.ModelBinding" },
                    ["ModelBinderAttribute"] = new[] { "Microsoft.AspNetCore.Mvc" },
                    ["BindingSource"] = new[] { "Microsoft.AspNetCore.Mvc.ModelBinding" },
                    ["ModelMetadata"] = new[] { "Microsoft.AspNetCore.Mvc.ModelBinding" },

                    // ASP.NET Core - Cors
                    ["CorsPolicy"] = new[] { "Microsoft.AspNetCore.Cors.Infrastructure" },
                    ["CorsPolicyBuilder"] = new[] { "Microsoft.AspNetCore.Cors.Infrastructure" },
                    ["EnableCors"] = new[] { "Microsoft.AspNetCore.Cors" },
                    ["DisableCors"] = new[] { "Microsoft.AspNetCore.Cors" },
                    ["EnableCorsAttribute"] = new[] { "Microsoft.AspNetCore.Cors" },
                    ["DisableCorsAttribute"] = new[] { "Microsoft.AspNetCore.Cors" },

                    // Entity Framework Core
                    ["DbContext"] = new[] { "Microsoft.EntityFrameworkCore" },
                    ["DbSet"] = new[] { "Microsoft.EntityFrameworkCore" },
                    ["DbContextOptions"] = new[] { "Microsoft.EntityFrameworkCore" },
                    ["Migration"] = new[] { "Microsoft.EntityFrameworkCore.Migrations" },
                    ["ModelBuilder"] = new[] { "Microsoft.EntityFrameworkCore" },
                    ["IEntityTypeConfiguration"] = new[] { "Microsoft.EntityFrameworkCore" },
                    ["EntityState"] = new[] { "Microsoft.EntityFrameworkCore" },
                    ["ChangeTracker"] = new[] { "Microsoft.EntityFrameworkCore.ChangeTracking" },

                    // WinForms - Core Controls
                    ["Form"] = new[] { "System.Windows.Forms" },
                    ["Button"] = new[] { "System.Windows.Forms" },
                    ["TextBox"] = new[] { "System.Windows.Forms" },
                    ["Label"] = new[] { "System.Windows.Forms" },
                    ["ListBox"] = new[] { "System.Windows.Forms" },
                    ["ComboBox"] = new[] { "System.Windows.Forms" },
                    ["CheckBox"] = new[] { "System.Windows.Forms" },
                    ["RadioButton"] = new[] { "System.Windows.Forms" },
                    ["GroupBox"] = new[] { "System.Windows.Forms" },
                    ["Panel"] = new[] { "System.Windows.Forms" },
                    ["PictureBox"] = new[] { "System.Windows.Forms" },
                    ["ProgressBar"] = new[] { "System.Windows.Forms" },
                    ["TrackBar"] = new[] { "System.Windows.Forms" },
                    ["DateTimePicker"] = new[] { "System.Windows.Forms" },
                    ["MonthCalendar"] = new[] { "System.Windows.Forms" },
                    ["NumericUpDown"] = new[] { "System.Windows.Forms" },
                    ["RichTextBox"] = new[] { "System.Windows.Forms" },
                    ["WebBrowser"] = new[] { "System.Windows.Forms" },
                    ["TabControl"] = new[] { "System.Windows.Forms" },
                    ["TabPage"] = new[] { "System.Windows.Forms" },
                    ["TreeView"] = new[] { "System.Windows.Forms" },
                    ["TreeNode"] = new[] { "System.Windows.Forms" },
                    ["ListView"] = new[] { "System.Windows.Forms" },
                    ["ListViewItem"] = new[] { "System.Windows.Forms" },
                    ["DataGridView"] = new[] { "System.Windows.Forms" },
                    ["DataGridViewRow"] = new[] { "System.Windows.Forms" },
                    ["DataGridViewCell"] = new[] { "System.Windows.Forms" },
                    ["MenuStrip"] = new[] { "System.Windows.Forms" },
                    ["ToolStrip"] = new[] { "System.Windows.Forms" },
                    ["StatusStrip"] = new[] { "System.Windows.Forms" },
                    ["ToolStripMenuItem"] = new[] { "System.Windows.Forms" },
                    ["ToolStripButton"] = new[] { "System.Windows.Forms" },
                    ["ContextMenuStrip"] = new[] { "System.Windows.Forms" },
                    ["SplitContainer"] = new[] { "System.Windows.Forms" },
                    ["FlowLayoutPanel"] = new[] { "System.Windows.Forms" },
                    ["TableLayoutPanel"] = new[] { "System.Windows.Forms" },
                    ["ToolTip"] = new[] { "System.Windows.Forms" },
                    ["Timer"] = new[] { "System.Windows.Forms", "System.Threading" },
                    ["NotifyIcon"] = new[] { "System.Windows.Forms" },
                    ["OpenFileDialog"] = new[] { "System.Windows.Forms" },
                    ["SaveFileDialog"] = new[] { "System.Windows.Forms" },
                    ["FolderBrowserDialog"] = new[] { "System.Windows.Forms" },
                    ["ColorDialog"] = new[] { "System.Windows.Forms" },
                    ["FontDialog"] = new[] { "System.Windows.Forms" },
                    ["MessageBox"] = new[] { "System.Windows.Forms" },
                    ["DialogResult"] = new[] { "System.Windows.Forms" },
                    ["Application"] = new[] { "System.Windows.Forms" },
                    ["Control"] = new[] { "System.Windows.Forms" },
                    ["UserControl"] = new[] { "System.Windows.Forms" },
                    ["Clipboard"] = new[] { "System.Windows.Forms" },

                    // WinForms - Drawing
                    ["Graphics"] = new[] { "System.Drawing" },
                    ["Pen"] = new[] { "System.Drawing" },
                    ["Brush"] = new[] { "System.Drawing" },
                    ["SolidBrush"] = new[] { "System.Drawing" },
                    ["Color"] = new[] { "System.Drawing" },
                    ["Font"] = new[] { "System.Drawing" },
                    ["Point"] = new[] { "System.Drawing" },
                    ["PointF"] = new[] { "System.Drawing" },
                    ["Size"] = new[] { "System.Drawing" },
                    ["SizeF"] = new[] { "System.Drawing" },
                    ["Rectangle"] = new[] { "System.Drawing" },
                    ["RectangleF"] = new[] { "System.Drawing" },
                    ["Image"] = new[] { "System.Drawing" },
                    ["Bitmap"] = new[] { "System.Drawing" },
                    ["Icon"] = new[] { "System.Drawing" },

                    // Newtonsoft.Json (Json.NET)
                    ["JsonConvert"] = new[] { "Newtonsoft.Json" },
                    ["JsonSerializer"] = new[] { "Newtonsoft.Json", "System.Text.Json" },
                    ["JObject"] = new[] { "Newtonsoft.Json.Linq" },
                    ["JArray"] = new[] { "Newtonsoft.Json.Linq" },
                    ["JToken"] = new[] { "Newtonsoft.Json.Linq" },
                    ["JValue"] = new[] { "Newtonsoft.Json.Linq" },
                    ["JProperty"] = new[] { "Newtonsoft.Json.Linq" },
                    ["JsonProperty"] = new[] { "Newtonsoft.Json" },
                    ["JsonIgnore"] = new[] { "Newtonsoft.Json" },
                    ["JsonSerializerSettings"] = new[] { "Newtonsoft.Json" },
                    ["JsonTextReader"] = new[] { "Newtonsoft.Json" },
                    ["JsonTextWriter"] = new[] { "Newtonsoft.Json" },

                    // Dapper
                    ["SqlMapper"] = new[] { "Dapper" },

                    // AutoMapper
                    ["IMapper"] = new[] { "AutoMapper" },
                    ["Profile"] = new[] { "AutoMapper" },
                    ["MapperConfiguration"] = new[] { "AutoMapper" },

                    // Serilog
                    ["Log"] = new[] { "Serilog" },
                    ["LoggerConfiguration"] = new[] { "Serilog" },

                    // FluentValidation
                    ["AbstractValidator"] = new[] { "FluentValidation" },
                    ["ValidationResult"] = new[] { "FluentValidation.Results" },
                    ["IValidator"] = new[] { "FluentValidation" },

                    // MediatR
                    ["IMediator"] = new[] { "MediatR" },
                    ["IRequest"] = new[] { "MediatR" },
                    ["IRequestHandler"] = new[] { "MediatR" },
                    ["INotification"] = new[] { "MediatR" },
                    ["INotificationHandler"] = new[] { "MediatR" },

                    // RestSharp
                    ["RestClient"] = new[] { "RestSharp" },
                    ["RestRequest"] = new[] { "RestSharp" },
                    ["RestResponse"] = new[] { "RestSharp" },

                    // StackExchange.Redis
                    ["ConnectionMultiplexer"] = new[] { "StackExchange.Redis" },
                    ["IDatabase"] = new[] { "StackExchange.Redis" },
                    ["IServer"] = new[] { "StackExchange.Redis" },
                    ["RedisKey"] = new[] { "StackExchange.Redis" },
                    ["RedisValue"] = new[] { "StackExchange.Redis" },

                    // Polly (Resilience)
                    ["Policy"] = new[] { "Polly" },
                    ["AsyncPolicy"] = new[] { "Polly" },
                    ["Context"] = new[] { "Polly" },

                    // NUnit / xUnit / MSTest
                    ["TestFixture"] = new[] { "NUnit.Framework" },
                    ["Test"] = new[] { "NUnit.Framework" },
                    ["SetUp"] = new[] { "NUnit.Framework" },
                    ["TearDown"] = new[] { "NUnit.Framework" },
                    ["Assert"] = new[] { "NUnit.Framework", "Xunit", "Microsoft.VisualStudio.TestTools.UnitTesting" },
                    ["Fact"] = new[] { "Xunit" },
                    ["Theory"] = new[] { "Xunit" },
                    ["InlineData"] = new[] { "Xunit" },
                    ["TestClass"] = new[] { "Microsoft.VisualStudio.TestTools.UnitTesting" },
                    ["TestMethod"] = new[] { "Microsoft.VisualStudio.TestTools.UnitTesting" },
                    ["TestInitialize"] = new[] { "Microsoft.VisualStudio.TestTools.UnitTesting" },
                    ["TestCleanup"] = new[] { "Microsoft.VisualStudio.TestTools.UnitTesting" },

                    // Moq
                    ["Mock"] = new[] { "Moq" },
                    ["It"] = new[] { "Moq" },
                    ["Times"] = new[] { "Moq" },

                    // Bogus (Fake Data)
                    ["Faker"] = new[] { "Bogus" },

                    // Hangfire (Background Jobs)
                    ["BackgroundJob"] = new[] { "Hangfire" },
                    ["RecurringJob"] = new[] { "Hangfire" },

                    // EPPlus (Excel)
                    ["ExcelPackage"] = new[] { "OfficeOpenXml" },
                    ["ExcelWorksheet"] = new[] { "OfficeOpenXml" },
                    ["ExcelRange"] = new[] { "OfficeOpenXml" },

                    // ClosedXML (Excel)
                    ["XLWorkbook"] = new[] { "ClosedXML.Excel" },
                    ["IXLWorksheet"] = new[] { "ClosedXML.Excel" },

                    // NodaTime
                    ["Instant"] = new[] { "NodaTime" },
                    ["LocalDate"] = new[] { "NodaTime" },
                    ["LocalDateTime"] = new[] { "NodaTime" },
                    ["ZonedDateTime"] = new[] { "NodaTime" },
                    ["Duration"] = new[] { "NodaTime" },

                    // Humanizer
                    ["Humanize"] = new[] { "Humanizer" },

                    // Swashbuckle (Swagger)
                    ["SwaggerDoc"] = new[] { "Swashbuckle.AspNetCore.Swagger" },
                    ["SwaggerGenOptions"] = new[] { "Swashbuckle.AspNetCore.SwaggerGen" },

                    // MassTransit
                    ["IBus"] = new[] { "MassTransit" },
                    ["IConsumer"] = new[] { "MassTransit" },

                    // RabbitMQ
                    ["IConnection"] = new[] { "RabbitMQ.Client" },
                    ["IModel"] = new[] { "RabbitMQ.Client" },
                    ["ConnectionFactory"] = new[] { "RabbitMQ.Client" },

                    // MongoDB
                    ["MongoClient"] = new[] { "MongoDB.Driver" },
                    ["IMongoDatabase"] = new[] { "MongoDB.Driver" },
                    ["IMongoCollection"] = new[] { "MongoDB.Driver" },
                    ["BsonDocument"] = new[] { "MongoDB.Bson" },

                    // Npgsql (PostgreSQL)
                    ["NpgsqlConnection"] = new[] { "Npgsql" },
                    ["NpgsqlCommand"] = new[] { "Npgsql" },

                    // MySqlConnector
                    ["MySqlConnection"] = new[] { "MySqlConnector" },
                    ["MySqlCommand"] = new[] { "MySqlConnector" },

                    // CsvHelper
                    ["CsvReader"] = new[] { "CsvHelper" },
                    ["CsvWriter"] = new[] { "CsvHelper" },
                    ["CsvConfiguration"] = new[] { "CsvHelper.Configuration" }
                };

                if (commonNamespaces.ContainsKey(identifier))
                {
                    foreach (var namespaceName in commonNamespaces[identifier])
                    {
                        if (!suggestedSet.Add(namespaceName))
                        {
                            continue;
                        }

                        var action = CreateAddUsingAction(document, namespaceName, sourceText, syntaxRoot);
                        if (action != null)
                        {
                            results.Add(action);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating add using actions: {ex.Message}");
            }

            return results;
        }

        private static async Task<IReadOnlyList<string>> FindNamespacesFromCompilationAsync(
            Document document,
            string identifier,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return Array.Empty<string>();
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var compilation = semanticModel?.Compilation;
            if (compilation == null)
            {
                return Array.Empty<string>();
            }

            var results = new HashSet<string>(StringComparer.Ordinal);
            var typeSymbols = compilation.GetSymbolsWithName(
                name => string.Equals(name, identifier, StringComparison.Ordinal),
                SymbolFilter.Type,
                cancellationToken);

            foreach (var symbol in typeSymbols.OfType<INamedTypeSymbol>())
            {
                var ns = symbol.ContainingNamespace?.ToDisplayString();
                if (!string.IsNullOrWhiteSpace(ns))
                {
                    results.Add(ns);
                }
            }

            var memberSymbols = compilation.GetSymbolsWithName(
                name => string.Equals(name, identifier, StringComparison.Ordinal),
                SymbolFilter.Member,
                cancellationToken);

            foreach (var symbol in memberSymbols.OfType<IMethodSymbol>())
            {
                if (!symbol.IsExtensionMethod)
                {
                    continue;
                }

                var ns = symbol.ContainingNamespace?.ToDisplayString();
                if (!string.IsNullOrWhiteSpace(ns))
                {
                    results.Add(ns);
                }
            }

            return results.OrderBy(r => r, StringComparer.Ordinal).ToArray();
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

                    var suggestedNamespaces = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var token in tokens)
                    {
                        var namespaces = await FindNamespacesFromCompilationAsync(
                            document,
                            token,
                            cancellationToken);
                        foreach (var namespaceName in namespaces)
                        {
                            suggestedNamespaces.Add(namespaceName);
                        }
                    }

                    foreach (var namespaceName in suggestedNamespaces)
                    {
                        if (existingUsings.Contains(namespaceName))
                        {
                            continue;
                        }

                        var action = CreateAddUsingAction(document, namespaceName, sourceText, syntaxRoot);
                        if (action != null && !results.Any(r => r.Title == action.Title))
                        {
                            results.Add(action);
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
