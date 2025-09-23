# SharpPad 项目总览

SharpPad 是一个面向 C# 开发者的代码实验与 AI 辅助平台，结合 Monaco 编辑器、Roslyn 编译服务与多实例管理，在浏览器或桌面端提供即时运行、补全与聊天体验。

## 解决方案结构
- `SharpPad/`：ASP.NET Core 8/9 Web 应用，提供代码执行、语言服务 API 与静态资源托管。
- `SharpPad.Desktop/`：基于 Avalonia 的跨平台桌面壳层，嵌入 WebView 并复用 Web 项目。
- `MonacoRoslynCompletionProvider/`：Roslyn 驱动的语言服务库，对外暴露补全、格式化与多文件分析能力。
- `Program.cs`：命令行入口演示 `InstanceManager` 的端口分配策略，便于调试多实例场景。
- `KingOfTool/`：示例脚本与配置集合，可导入到应用中快速体验常用片段。

## Web 应用 (SharpPad)

### 后端 API
- `CompletionController` 调用 MonacoRoslynCompletionProvider，提供补全、签名提示、Hover、格式化、语义 Token 与多文件诊断。
- `CodeRunController` 使用 `CodeRunner` 运行单文件或多文件 C# 程序，通过通道与 SSE 流式推送输出和错误。
- `ChatController` 作为 AI 对话代理，从 `X-Endpoint` 头读取目标模型地址，保持授权信息和响应流。

### 框架配置
- `Program.cs` 结合 `InstanceManager` 自动选择端口，开启静态文件、CORS、Swagger 与控制器路由。
- 依赖 `Microsoft.AspNetCore.Mvc.NewtonsoftJson`、`Swashbuckle` 等组件，并在 `SharpPad.csproj` 中复制运行期 DLL 与资源。
- `Services/InstanceManager` 默认占用 5090 端口，检测冲突后回退到可用端口，同时提供占用端口查询。

### 前端模块
- `wwwroot/index.js` 作为入口，按需加载 Monaco、初始化文件系统、NuGet 管理、执行管线与通知。
- `editor/` 管理编辑器状态与命令绑定，`execution/` 负责运行、输出面板和状态提示。
- `fileSystem/` 维护本地存储、导入导出与目录视图，支持多目录和只看当前目录模式。
- `components/nuget/` 构建包引用 UI，`chat/` 与 `utils/` 提供 AI 交互、API 调用与公共工具。
- 静态资源包含 `monaco-editor`、样式、Markdown 和图标，并支持多模型切换与语义着色。

## 桌面宿主 (SharpPad.Desktop)
- 使用 Avalonia 11 + WebView.Avalonia，Windows 目标编译为 `net9.0`，非 Windows 编译为 `net8.0`。
- `App.axaml` 与 `MainWindow` 配置 WebView，配合 `WebServerManager` 启动/停止嵌入的 ASP.NET Core 服务。
- `WebServerManager` 处理发布包与开发环境路径，托管 `SharpPad.Program.ConfigureServices/Configure`。
- `SharpPad.Desktop.csproj` 发布阶段复制 `wwwroot` 与 `appsettings.json`，并清理多余语言资源。

## MonacoRoslynCompletionProvider
- 以 .NET 8/9 为目标，引用 Microsoft.CodeAnalysis 4.14 系列包并使用 `IgnoresAccessChecksTo` 解锁内部特性。
- `MonacoRequestHandler` 集成补全、签名帮助、Hover、Definition、语义 Token、格式化与多文件 CodeCheck。
- `CodeRunner` 支持多文件运行、NuGet 包解析、语言版本选择与流式回调，用于 Web 与桌面端的执行功能。

## 多实例与工具
- `SharpPad.Services.InstanceManager` 与桌面端同名实现负责端口探测，确保主实例 5090、其余实例使用动态端口。
- 根目录 `Program.cs` 和 `TestInstanceManager.cs` 可用于验证端口分配逻辑，`TestInstance.csproj` 提供轻量入口。
- `publish/`、`SharpPad.app/` 与脚本如 `package-macos.sh`、`docker-compose.yml` 帮助分发与容器化部署。

## 构建与运行
- 恢复依赖：`dotnet restore SharpPad.sln`
- 编译调试：`dotnet build SharpPad.sln -c Debug`
- 启动 Web：`dotnet run --project SharpPad/SharpPad.csproj`
- 启动桌面：`dotnet run --project SharpPad.Desktop/SharpPad.Desktop.csproj`
- 容器体验：在根目录执行 `docker compose up --build`

## 开发约定
- C# 使用四空格缩进，类型与公共成员使用 PascalCase，参数与局部变量使用 camelCase，私有字段 `_camelCase`。
- 控制器保持精简，复杂逻辑放入服务或独立模块，优先使用异步 API。
- 静态资源、文件与目录命名保持英文，并避免泄露敏感配置；生产密钥放入安全存储或环境变量。
- 新功能优先复用现有服务与前端模块，跨端共用逻辑置于 `SharpPad` 项目内。

## 测试与质量
- 目前尚未建立单元测试项目，新增覆盖建议使用 xUnit 并创建 `SharpPad.Tests/*Tests.cs`。
- 建议在提交前执行 `dotnet build` 或 `dotnet test`，为 AI 相关改动补充回归验证脚本。

## 文档与示例
- `README.md`（中文）与 `README_EN.md` 提供快速上手与特性说明，`README-Desktop.md` 针对桌面版本。
- `KingOfTool` 目录收录可导入的示例脚本、调试指南与多文件样例。
- `RoslynPad技术研究报告.md` 记录语言服务调研结果，可用于扩展 Monaco 能力。
