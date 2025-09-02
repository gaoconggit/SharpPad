# RoslynPad 技术研究报告
*RoslynPad 代码分析功能实现深度调研*

## 项目概述

RoslynPad 是一个基于 Microsoft.CodeAnalysis (Roslyn) 的强大代码编辑器项目，提供了类似 LinqPad 的交互式 C# 开发环境。本报告深入分析了 RoslynPad 如何使用 Roslyn API 实现各种代码分析功能。

**研究目的：** 为基于 Monaco Editor + ASP.NET Core 的 C# 代码编辑器项目（SharpPad）提供技术参考，扩展更多 Provider 功能。

---

## 1. 架构设计和核心组件

### 1.1 项目架构

RoslynPad 采用模块化设计，主要由以下组件构成：

- **RoslynPad.Roslyn**: 核心 Roslyn 服务封装库
- **RoslynPad.Editor**: 编辑器实现（Windows/Avalonia 版本）
- **RoslynPad.Common.UI**: 共享 UI 组件和视图模型
- **RoslynPad.Build**: 编译和执行服务

### 1.2 核心类结构

#### RoslynHost 类
```csharp
public class RoslynHost : IRoslynHost
{
    private readonly ConcurrentDictionary<DocumentId, RoslynWorkspace> _workspaces;
    private readonly CompositionHost _compositionContext;
    
    public HostServices HostServices { get; }
    public ParseOptions ParseOptions { get; }
    public CompilationOptions CompilationOptions { get; }
}
```

**关键特性：**
- 管理多个 RoslynWorkspace 实例
- 使用 MEF (Managed Extensibility Framework) 进行依赖注入
- 提供统一的服务获取接口：`GetService<T>()`

#### RoslynWorkspace 类
```csharp
public class RoslynWorkspace : Workspace
{
    public DocumentId? OpenDocumentId { get; private set; }
    public RoslynHost? RoslynHost { get; }
}
```

---

## 2. 代码完成 (Code Completion) 功能实现

### 2.1 实现架构

RoslynPad 通过 `RoslynCodeEditorCompletionProvider` 类实现代码完成功能：

```csharp
public sealed class RoslynCodeEditorCompletionProvider : ICodeEditorCompletionProvider
{
    private readonly DocumentId _documentId;
    private readonly IRoslynHost _roslynHost;
    private readonly SnippetInfoService _snippetService;
}
```

### 2.2 核心实现流程

#### 获取完成项
```csharp
public async Task<CompletionResult> GetCompletionData(int position, char? triggerChar, bool useSignatureHelp)
{
    var document = _roslynHost.GetDocument(_documentId);
    var completionService = CompletionService.GetService(document);
    
    var completionTrigger = GetCompletionTrigger(triggerChar);
    var data = await completionService.GetCompletionsAsync(
        document,
        position,
        completionTrigger
    ).ConfigureAwait(false);
    
    return new CompletionResult(data, overloadProvider);
}
```

#### 完成项数据结构
```csharp
public sealed class RoslynCompletionData : ICompletionData
{
    private readonly CompletionItem _item;
    private readonly Document _document;
    private readonly Glyph _glyph;
    
    public async void Complete(TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        var completionService = CompletionService.GetService(_document);
        var changes = await completionService.GetChangeAsync(_document, _item, null);
        // 应用文本更改...
    }
}
```

### 2.3 触发机制

- **字符触发**：`.`, `(`, `[`, `<` 等字符
- **手动调用**：Ctrl+空格键
- **智能感知**：基于上下文自动触发

### 2.4 性能优化技巧

1. **预热机制**：在应用启动时初始化 Provider
2. **缓存策略**：缓存常用的完成项
3. **异步处理**：使用 `ConfigureAwait(false)` 避免死锁

---

## 3. 定义跳转 (Go to Definition) 功能实现

### 3.1 Roslyn API 核心方法

虽然在 RoslynPad 的搜索结果中没有找到显式的 "GoToDefinition" 实现，但基于 Roslyn 官方文档，实现方式如下：

```csharp
// 基于 SymbolFinder API 实现
public async Task<ISymbol?> FindDefinitionAsync(Document document, int position)
{
    var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
        document, position, cancellationToken);
    
    if (symbol != null)
    {
        var definition = await SymbolFinder.FindSourceDefinitionAsync(
            symbol, document.Project.Solution, cancellationToken);
        return definition ?? symbol;
    }
    
    return null;
}
```

### 3.2 导航服务

RoslynPad 提供了 `DocumentNavigationService` 来处理文档内导航：

```csharp
[ExportWorkspaceService(typeof(IDocumentNavigationService))]
internal sealed class DocumentNavigationService : IDocumentNavigationService
{
    public Task<bool> CanNavigateToSpanAsync(Workspace workspace, DocumentId documentId, 
        TextSpan textSpan, bool allowInvalidSpan, CancellationToken cancellationToken) 
        => Task.FromResult(true);
        
    public Task<bool> CanNavigateToPositionAsync(Workspace workspace, DocumentId documentId, 
        int position, int virtualSpace, bool allowInvalidPosition, CancellationToken cancellationToken) 
        => Task.FromResult(true);
}
```

### 3.3 实现要点

1. **符号解析**：在指定位置获取符号信息
2. **定义查找**：通过 `SymbolFinder.FindSourceDefinitionAsync` 查找源定义
3. **导航逻辑**：处理跨文件、跨项目的导航

---

## 4. 引用查找 (Find References) 功能实现

### 4.1 Roslyn API 实现

```csharp
public async Task<IEnumerable<ReferencedSymbol>> FindReferencesAsync(
    ISymbol symbol, Solution solution)
{
    var references = await SymbolFinder.FindReferencesAsync(
        symbol, solution, cancellationToken);
    
    return references;
}
```

### 4.2 引用位置数据结构

```csharp
public class ReferenceLocation
{
    public Document Document { get; }
    public Location Location { get; }
    public bool IsImplicit { get; }
    public bool IsCandidateLocation { get; }
    public CandidateReason CandidateReason { get; }
    public IAliasSymbol Alias { get; }
}
```

### 4.3 搜索范围控制

- **项目内搜索**：`FindReferencesAsync(symbol, solution, documents)`
- **解决方案搜索**：`FindReferencesAsync(symbol, solution)`
- **筛选功能**：按文档类型、访问级别筛选

---

## 5. 代码诊断 (Diagnostics) 功能实现

### 5.1 诊断更新器

RoslynPad 使用 `DiagnosticsUpdater` 来管理诊断信息：

```csharp
internal sealed class DiagnosticsUpdater
{
    private readonly IDiagnosticAnalyzerService _diagnosticAnalyzerService;
    private readonly HashSet<DiagnosticData> _currentDiagnostics;
    
    private async Task UpdateDiagnosticsAsync(Document document, CancellationToken cancellationToken)
    {
        var diagnostics = await GetDiagnostics(document, cancellationToken);
        
        lock (_lock)
        {
            var addedDiagnostics = diagnostics.Where(d => 
                !_currentDiagnostics.Contains(d) && 
                !DisabledDiagnostics.Contains(d.Id)).ToHashSet();
                
            // 更新诊断信息...
        }
    }
}
```

### 5.2 诊断分析服务

```csharp
[Export(typeof(IDiagnosticAnalyzerService)), Shared]
internal sealed class DiagnosticAnalyzerService : IDiagnosticAnalyzerService
{
    public async Task<ImmutableArray<DiagnosticData>> GetDiagnosticsForSpanAsync(
        TextDocument document, TextSpan? range, CancellationToken cancellationToken)
    {
        var diagnostics = await inner.GetDiagnosticsForSpanAsync(
            document, range, DiagnosticKind.All, cancellationToken);
        
        return ConvertDiagnostics(diagnostics);
    }
}
```

### 5.3 诊断可视化

在编辑器中通过 `TextMarkerService` 显示诊断标记：

```csharp
private void UpdateDiagnostics(ImmutableArray<DiagnosticData> diagnostics)
{
    foreach (var diagnosticData in diagnostics)
    {
        var span = diagnosticData.GetTextSpan();
        var marker = _textMarkerService.TryCreate(span.Value.Start, span.Value.Length);
        
        if (marker != null)
        {
            marker.MarkerColor = GetDiagnosticsColor(diagnosticData);
            marker.ToolTip = diagnosticData.Message;
        }
    }
}

private static Color GetDiagnosticsColor(DiagnosticData diagnosticData)
{
    return diagnosticData.Severity switch
    {
        DiagnosticSeverity.Info => Colors.LimeGreen,
        DiagnosticSeverity.Warning => Colors.DodgerBlue,
        DiagnosticSeverity.Error => Colors.Red,
        _ => Colors.Gray
    };
}
```

---

## 6. 重命名 (Rename) 功能实现

### 6.1 重命名流程

RoslynPad 的重命名功能实现：

```csharp
private async Task RenameSymbolAsync()
{
    var host = MainViewModel.RoslynHost;
    var document = host.GetDocument(DocumentId);
    
    // 1. 获取光标位置的符号
    var symbol = await RenameHelper.GetRenameSymbol(document, position, cancellationToken);
    
    if (symbol == null) return;
    
    // 2. 显示重命名对话框
    var dialog = _serviceProvider.GetRequiredService<IRenameSymbolDialog>();
    dialog.Initialize(symbol.Name);
    await dialog.ShowAsync();
    
    if (dialog.ShouldRename)
    {
        // 3. 执行重命名操作
        var newSolution = await Renamer.RenameSymbolAsync(
            document.Project.Solution, 
            symbol, 
            new SymbolRenameOptions(), 
            dialog.SymbolName ?? string.Empty);
            
        var newDocument = newSolution.GetDocument(DocumentId);
        host.UpdateDocument(newDocument!);
    }
}
```

### 6.2 符号识别辅助类

```csharp
public static class RenameHelper
{
    public static async Task<ISymbol?> GetRenameSymbol(
        Document document, int position, CancellationToken cancellationToken = default)
    {
        var token = await document.GetTouchingWordAsync(position, cancellationToken);
        return token != default 
            ? await GetRenameSymbol(document, token, cancellationToken)
            : null;
    }
    
    public static async Task<ISymbol?> GetRenameSymbol(
        Document document, SyntaxToken triggerToken, CancellationToken cancellationToken)
    {
        var syntaxFactsService = document.Project.Services.GetRequiredService<ISyntaxFactsService>();
        if (syntaxFactsService.IsReservedOrContextualKeyword(triggerToken))
        {
            return null; // 不能重命名关键字
        }
        
        // 获取语义模型并查找符号
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        var symbol = semanticModel?.GetSymbolInfo(triggerToken.Parent, cancellationToken).Symbol;
        
        return symbol?.CanBeReferencedByName == true ? symbol : null;
    }
}
```

### 6.3 重命名选项

Roslyn 提供了丰富的重命名选项：

```csharp
public class SymbolRenameOptions
{
    public bool RenameOverloads { get; set; } = false;
    public bool RenameInStrings { get; set; } = false;
    public bool RenameInComments { get; set; } = false;
    public bool RenameFile { get; set; } = false;
}
```

---

## 7. 符号查找 (Document Symbols) 功能实现

### 7.1 文档符号提取

基于 Roslyn 的 `SymbolFinder` API 实现：

```csharp
public async Task<IEnumerable<ISymbol>> GetDocumentSymbolsAsync(Document document)
{
    var symbols = new List<ISymbol>();
    
    // 获取文档中的所有声明
    var declarations = await SymbolFinder.FindSourceDeclarationsAsync(
        document.Project, 
        predicate: name => true, 
        filter: SymbolFilter.All,
        cancellationToken);
    
    // 过滤出当前文档的符号
    foreach (var symbol in declarations)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.SourceTree == await document.GetSyntaxTreeAsync())
            {
                symbols.Add(symbol);
                break;
            }
        }
    }
    
    return symbols.OrderBy(s => s.Locations.First().SourceSpan.Start);
}
```

### 7.2 符号分类和过滤

```csharp
public enum SymbolFilter
{
    None = 0,
    Namespace = 1,
    Type = 2,
    Member = 4,
    TypeAndMember = Type | Member,
    All = Namespace | TypeAndMember
}
```

### 7.3 符号显示格式

```csharp
public class DocumentSymbolInfo
{
    public string Name { get; set; }
    public SymbolKind Kind { get; set; }
    public TextSpan Span { get; set; }
    public Glyph Glyph { get; set; }
    public IList<DocumentSymbolInfo> Children { get; set; }
}
```

---

## 8. 代码格式化 (Formatting) 功能实现

### 8.1 格式化实现

RoslynPad 的格式化功能非常简洁：

```csharp
private async Task FormatDocumentAsync()
{
    var document = MainViewModel.RoslynHost.GetDocument(DocumentId);
    var formattedDocument = await Formatter.FormatAsync(document!);
    MainViewModel.RoslynHost.UpdateDocument(formattedDocument);
}
```

### 8.2 格式化选项

```csharp
public class FormattingOptions
{
    public bool InsertSpaceAfterKeywordsInControlFlowStatements { get; set; } = true;
    public bool InsertSpaceAfterSemicolonsInForStatement { get; set; } = true;
    public bool InsertSpaceAfterColonInBaseTypeDeclaration { get; set; } = true;
    public bool InsertSpaceBeforeOpenSquareBracket { get; set; } = false;
    // ... 更多格式化选项
}
```

### 8.3 自动格式化

```csharp
// 在注释/取消注释操作后自动格式化
if (action == CommentAction.Uncomment && MainViewModel.Settings.FormatDocumentOnComment)
{
    await FormatDocumentAsync();
}
```

---

## 9. 悬停信息 (Hover) 功能实现

### 9.1 QuickInfo 提供器

```csharp
[Export(typeof(IQuickInfoProvider)), Shared]
internal sealed class QuickInfoProvider : IQuickInfoProvider
{
    public async Task<QuickInfoItem?> GetItemAsync(
        Document document,
        int position, 
        CancellationToken cancellationToken = default)
    {
        var service = QuickInfoService.GetService(document);
        if (service == null) return null;
        
        var info = await service.GetQuickInfoAsync(
            document, position, cancellationToken);
            
        return info;
    }
}
```

### 9.2 悬停内容构建

```csharp
public class QuickInfoItem
{
    public TextSpan TextSpan { get; set; }
    public ImmutableArray<TaggedText> Tags { get; set; }
    public ImmutableArray<TaggedText> Description { get; set; }
    public ImmutableArray<TaggedText> DocumentationComment { get; set; }
}
```

---

## 10. 签名帮助 (Signature Help) 功能实现

### 10.1 聚合签名帮助提供器

```csharp
[Export(typeof(ISignatureHelpProvider)), Shared]
internal sealed class AggregateSignatureHelpProvider : ISignatureHelpProvider
{
    private readonly ImmutableArray<Microsoft.CodeAnalysis.SignatureHelp.ISignatureHelpProvider> _providers;
    
    public bool IsTriggerCharacter(char ch)
    {
        return _providers.Any(p => p.IsTriggerCharacter(ch));
    }
    
    public async Task<SignatureHelpItems?> GetItemsAsync(
        Document document, 
        int position,
        SignatureHelpTriggerInfo triggerInfo, 
        CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            var items = await provider.GetItemsAsync(
                document, position, triggerInfo, cancellationToken);
            if (items != null) return items;
        }
        return null;
    }
}
```

### 10.2 触发字符和重触发

```csharp
public interface ISignatureHelpProvider
{
    bool IsTriggerCharacter(char ch);
    bool IsRetriggerCharacter(char ch);
    Task<SignatureHelpItems?> GetItemsAsync(...);
}
```

常见的触发字符：`(`, `,`, `<`

---

## 11. 异步处理和性能优化

### 11.1 异步模式

RoslynPad 大量使用异步模式来避免 UI 阻塞：

```csharp
// 正确的异步调用模式
var result = await completionService.GetCompletionsAsync(
    document, position, trigger).ConfigureAwait(false);

// 避免死锁的配置
await someMethod().ConfigureAwait(false);
```

### 11.2 取消令牌使用

```csharp
public async Task<T> GetDataAsync(CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    
    var result = await someOperation(cancellationToken);
    
    cancellationToken.ThrowIfCancellationRequested();
    return result;
}
```

### 11.3 缓存策略

- **工作区缓存**：使用 `ConcurrentDictionary<DocumentId, RoslynWorkspace>` 缓存工作区
- **服务缓存**：通过 MEF 实现单例服务
- **完成项缓存**：缓存常用的代码完成项

---

## 12. 与前端编辑器的集成方式

### 12.1 数据传输格式

对于 Web 应用，需要将 Roslyn 的复杂对象序列化为 JSON：

```csharp
public class CompletionItemDto
{
    public string Label { get; set; }
    public string Detail { get; set; }
    public string Documentation { get; set; }
    public string InsertText { get; set; }
    public CompletionItemKind Kind { get; set; }
    public TextEditDto TextEdit { get; set; }
}

public class TextEditDto
{
    public RangeDto Range { get; set; }
    public string NewText { get; set; }
}
```

### 12.2 API 端点设计

基于 RoslynPad 的设计，建议的 API 结构：

```csharp
[ApiController]
[Route("api/[controller]")]
public class CodeAnalysisController : ControllerBase
{
    [HttpPost("completion")]
    public async Task<CompletionResult> GetCompletion([FromBody] CompletionRequest request)
    {
        // 实现代码完成逻辑
    }
    
    [HttpPost("hover")]
    public async Task<HoverResult> GetHover([FromBody] HoverRequest request)
    {
        // 实现悬停信息逻辑
    }
    
    [HttpPost("signature-help")]
    public async Task<SignatureHelpResult> GetSignatureHelp([FromBody] SignatureHelpRequest request)
    {
        // 实现签名帮助逻辑
    }
    
    [HttpPost("goto-definition")]
    public async Task<DefinitionResult> GotoDefinition([FromBody] DefinitionRequest request)
    {
        // 实现定义跳转逻辑
    }
    
    [HttpPost("find-references")]
    public async Task<ReferencesResult> FindReferences([FromBody] ReferencesRequest request)
    {
        // 实现引用查找逻辑
    }
    
    [HttpPost("rename")]
    public async Task<RenameResult> RenameSymbol([FromBody] RenameRequest request)
    {
        // 实现重命名逻辑
    }
    
    [HttpPost("diagnostics")]
    public async Task<DiagnosticsResult> GetDiagnostics([FromBody] DiagnosticsRequest request)
    {
        // 实现诊断逻辑
    }
    
    [HttpPost("document-symbols")]
    public async Task<DocumentSymbolsResult> GetDocumentSymbols([FromBody] DocumentSymbolsRequest request)
    {
        // 实现文档符号逻辑
    }
    
    [HttpPost("format")]
    public async Task<FormatResult> FormatDocument([FromBody] FormatRequest request)
    {
        // 实现格式化逻辑
    }
}
```

---

## 13. 实践建议和最佳实践

### 13.1 架构建议

1. **分离关注点**：将 Roslyn 服务封装在独立的库中
2. **依赖注入**：使用 MEF 或内置 DI 容器管理服务生命周期
3. **工作区管理**：为每个编辑会话维护独立的工作区
4. **异步优先**：所有 Roslyn API 调用都应该是异步的

### 13.2 性能优化

1. **预热策略**：应用启动时初始化常用服务
2. **批处理**：合并多个请求减少往返次数
3. **缓存机制**：缓存编译结果和符号信息
4. **内存管理**：及时释放大型对象，避免内存泄漏

### 13.3 错误处理

```csharp
public async Task<Result<T>> SafeExecuteAsync<T>(Func<Task<T>> operation)
{
    try
    {
        var result = await operation();
        return Result<T>.Success(result);
    }
    catch (OperationCanceledException)
    {
        return Result<T>.Cancelled();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Operation failed");
        return Result<T>.Failure(ex.Message);
    }
}
```

### 13.4 测试策略

1. **单元测试**：测试各个服务的核心逻辑
2. **集成测试**：测试 Roslyn 与编辑器的集成
3. **性能测试**：验证大型文件的处理性能
4. **端到端测试**：测试完整的用户场景

---

## 14. 扩展功能建议

基于 RoslynPad 的实现，可以考虑添加以下功能：

### 14.1 代码重构

- **提取方法**：`ExtractMethodRefactoringProvider`
- **内联变量**：`InlineVariableRefactoringProvider`
- **移动类型**：`MoveTypeRefactoringProvider`

### 14.2 代码生成

- **生成构造函数**：基于字段自动生成构造函数
- **生成属性**：为字段生成对应的属性
- **实现接口**：自动实现接口的所有成员

### 14.3 智能感知增强

- **Import 建议**：自动建议缺失的 using 语句
- **拼写检查**：检查标识符的拼写
- **代码度量**：计算代码复杂度和质量指标

---

## 15. 关键技术要点总结

### 15.1 Roslyn API 核心类

- **Document**: 表示源文件
- **Solution**: 表示整个解决方案
- **Project**: 表示项目
- **SemanticModel**: 提供语义分析信息
- **SyntaxTree**: 表示语法树
- **ISymbol**: 表示代码符号（类、方法、变量等）

### 15.2 重要服务接口

- **CompletionService**: 代码完成服务
- **IQuickInfoProvider**: 悬停信息提供器
- **ISignatureHelpProvider**: 签名帮助提供器
- **IDiagnosticAnalyzerService**: 诊断分析服务
- **IDocumentNavigationService**: 文档导航服务

### 15.3 设计模式应用

- **策略模式**：不同类型的完成提供器
- **观察者模式**：诊断信息的更新通知
- **工厂模式**：服务的创建和管理
- **装饰器模式**：功能的增强和扩展

---

## 16. 参考资源

### 16.1 官方资源

- [Microsoft.CodeAnalysis API 文档](https://docs.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis)
- [Roslyn 源代码](https://github.com/dotnet/roslyn)
- [Roslyn 示例和教程](https://github.com/dotnet/roslyn/wiki/Samples-and-Walkthroughs)

### 16.2 社区资源

- [RoslynPad 项目](https://github.com/roslynpad/roslynpad)
- [OmniSharp 项目](https://github.com/OmniSharp/omnisharp-roslyn)
- [Roslyn API 使用示例](http://source.roslyn.io/)

### 16.3 相关工具

- **Monaco Editor**: Web 代码编辑器
- **Language Server Protocol**: 语言服务协议
- **MEF**: Managed Extensibility Framework

---

## 结论

RoslynPad 项目为我们展示了如何优雅地使用 Microsoft.CodeAnalysis (Roslyn) API 构建功能丰富的代码编辑器。通过深入分析其架构设计和实现细节，我们可以为 SharpPad 项目的扩展提供有力的技术支撑。

**主要收获：**

1. **模块化设计**：将不同功能分离到独立的服务中
2. **异步优先**：所有 I/O 操作都应该是异步的
3. **缓存策略**：合理使用缓存提升性能
4. **错误处理**：完善的异常处理机制
5. **扩展性**：通过 MEF 实现插件化架构

通过采用这些最佳实践，可以构建出高性能、可扩展的 C# 代码分析服务，为用户提供卓越的开发体验。

---

*报告完成日期：2025年1月*
*研究范围：RoslynPad v7.0+ 相关功能*
*目标项目：SharpPad (Monaco Editor + ASP.NET Core)*