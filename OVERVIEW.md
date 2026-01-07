# SharpPad 项目概览

SharpPad 是基于 Roslyn 与 Monaco Editor 的在线/桌面 C# 代码运行与 AI 增强体验工具，提供即时编译运行、多文件/多项目形态、NuGet 依赖管理、AI Chat/补全/编辑等能力。本概览从代码结构角度梳理关键模块和运行链路，便于快速理解并着手开发。

## 解决方案结构
- **SharpPad** (`SharpPad/`)：ASP.NET Core Web API + 静态前端。负责对外接口、代码编译执行、AI 代理、NuGet 管理以及前端静态资源承载。
- **SharpPad.Desktop** (`SharpPad.Desktop/`)：Avalonia 桌面外壳，内嵌 WebView 并自托管 Web API，使客户端打包后可离线运行。
- **SharpPad.ExecutionHost** (`SharpPad.ExecutionHost/`)：隔离的执行宿主，负责加载/运行编译产物并处理输入输出、STA 线程等细节。
- **MonacoRoslynCompletionProvider** (`MonacoRoslynCompletionProvider/`)：Roslyn 驱动的语言服务与运行时支撑，提供补全、语义高亮、代码检查、包解析及编译/执行管线。
- **KingOfTool** (`KingOfTool/`)：示例脚本与调试文档集合，可在应用内导入验证功能。

## 核心后端 (SharpPad)
- **启动与端口**：`Program.cs` 通过 `InstanceManager` 动态选择端口并设置 `ASPNETCORE_URLS`，启用 CORS、Swagger、静态文件与控制器路由。
- **代码运行**：`CodeRunController` 暴露 `/api/CodeRun/run` SSE 接口，调用 `MonacoRoslynCompletionProvider.Api.CodeRunner` 编译/运行单文件或多文件请求；管道支持 NuGet 包、项目类型（console/webapi/winforms/avalonia）与取消流控制。
- **AI/语言服务**：`CompletionController` 提供补全、签名帮助、Hover、语义 token、代码检查/格式化、代码行动等端点；`ChatController` 作为流式反向代理，将请求转发到 `X-Endpoint` 指定的模型服务。
- **包与文件管理**：`PackageSourceController` 和 `NugetProxyController` 处理 NuGet 源维护与代理下载；`FileImportController` 校验并导入 JSON 目录配置；`wwwroot/components/nuget` 提供前端管理 UI。
- **前端静态资源**：`wwwroot/` 包含 Monaco Editor、AI 辅助脚本（`editor/aiEdit.js`）、运行器（`execution/runner.js`）、文件树（`fileSystem/fileManager.js`）、聊天面板（`chat/chat.js`）等，入口页面为 `wwwroot/index.html`。

## 桌面外壳 (SharpPad.Desktop)
- **宿主形态**：Avalonia 11 应用 (`Program.cs`, `App.axaml`)，使用 `WebView.Avalonia` 渲染 Web 前端。
- **内置服务**：`Services/WebServerManager` 在启动时托管 `SharpPad.Program`（与 Web 版同一中间件配置），复制 `wwwroot`/`appsettings.json` 至发布目录，并复用 `InstanceManager` 处理多实例端口。
- **视图与桥接**：`Views/MainWindow.axaml` + `ViewModels/MainWindowViewModel` 管理加载流程、WebView 可见性与退出；`Interop/WebViewBridge` 预留桌面与网页的消息通道。

## 执行宿主 (SharpPad.ExecutionHost)
- 由 `MonacoRoslynCompletionProvider.Api.CodeRunner` 调用，负责在独立 `AssemblyLoadContext` 中加载编译产物。
- 处理控制台输入/输出重定向、`Console.ReadKey` 兼容、WinForms/Avalonia 场景下的 STA 线程，以及原生库探测（Skia/HarfBuzz）和路径注入。
- 通过命令行参数接收程序集路径、工作目录、入口参数等；异常被捕获并回传到调用端。

## 语言服务与编译管线 (MonacoRoslynCompletionProvider)
- **编译/运行**：`Api/CodeRunner` 生成 Roslyn 语法树、解析 NuGet 依赖（`NugetHelper` 与 `PackageSourceManager`）、按项目类型配置 `OutputKind`，并在需要时寻找 `SharpPad.ExecutionHost.dll` 作为运行器。
- **智能提示**：`CompletionWorkspace` 维护 `AdhocWorkspace`、引用缓存与 XML 文档解析；`TabCompletionProvider`、`SignatureHelpProvider`、`HoverInformationProvider`、`DefinitionProvider`、`SemanticTokensProvider` 等提供 Monaco 侧所需的数据。
- **多文件支持**：`Api/MultiFile*Request` 与对应 handler 支持跨文件的编译、诊断与语义信息收集。
- **代码检查/格式化**：`CodeCheckProvider` 结合 Roslyn 分析器输出诊断；`MonacoRequestHandler.FormatCode` 使用 Roslyn 格式化 API。

## 前端形态
- **编辑/运行体验**：Monaco 初始化由 `editor/index.mjs` 驱动，`execution/outputPanel.js` 处理 SSE 输出流；`utils/multiFileHelper.js` 协助多文件布局；`semanticColoring.js` 接入语义 token。
- **AI 与工具**：`chat/chat.js` 对接 `/v1/chat/completions` 流式响应；`editor/aiEdit.js`、`editor/codeActionProvider.js` 集成 AI 编辑与 Roslyn 代码行动；NuGet 管理界面在 `components/nuget/`。
- **界面样式**：`styles/` 目录覆盖主题、文件树、对话框、滚动条等视觉元素。

## 示例与辅助资源
- **KingOfTool**：内置的示例目录与配置 (`KingOfTool.json`、`MultiFileExample/` 等)，用于演示多文件工程、WebAPI/WinForms/Avalonia 运行及调试入口 (`Debugger.md`)。
- **Docker 与发布**：`Dockerfile`/`docker-compose.yml` 提供容器化运行示例并持久化 NuGet 缓存；桌面项目的发布任务会在打包时复制 Web 静态资源。

## 开发提示
- 目标框架为 `.NET 10`（Windows 下使用 `net10.0-windows`），推荐 VS 2026 或 `dotnet` SDK；多实例场景自动选取空闲端口。
- 如需扩展接口或运行管线，优先复用 `MonacoRoslynCompletionProvider` 中的请求/响应模型，保持前后端协议一致。

