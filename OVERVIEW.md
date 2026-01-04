# SharpPad Overview

面向 C# 开发者的代码实验与 AI 助手平台，融合 Monaco 编辑器、Roslyn 语言服务、NuGet 包管理与跨端壳层（Web + Avalonia 桌面）。

## 代码结构速览
- `SharpPad/`：ASP.NET Core 9 Web 应用。`Controllers` 提供补全/运行/聊天/NuGet/文件导入 API，`Services/InstanceManager` 负责 5090 起的端口探测与占用查询，`wwwroot/` 存放前端代码与静态资源。
- `MonacoRoslynCompletionProvider/`：Roslyn 驱动的语言服务与执行管线，涵盖补全、签名、Hover、Definition、语义 Token、格式化、多文件诊断与 `Api/CodeRunner`。
- `SharpPad.Desktop/`：Avalonia 11 桌面宿主，`Services/WebServerManager` 启动/停止嵌入式 Web 站点，`Views/MainWindow` 承载 WebView。
- `SharpPad.ExecutionHost/`：独立执行宿主，加载已编译的用户程序集，处理 STA 切换、原生库探测（Skia/HarfBuzz）与标准输入提示。
- 其他：`KingOfTool/` 示例脚本与配置，`memory/` 记录特定需求，根目录 `Program.cs`/`TestInstanceManager` 用于实例管理验证。

## 关键运行流
- **编辑与语言服务**：前端 Monaco 通过 `CompletionController` 调用 MonacoRoslynCompletionProvider（`MonacoRequestHandler` 及相关 Provider）获取补全、签名、Hover、语义 Token 等结果。
- **代码执行**：`CodeRunController` 调用 `Api/CodeRunner` 编译/运行单文件或多文件，输出通过 SSE/通道流式返回；需要时由 `SharpPad.ExecutionHost` 附加进程隔离与原生库加载。
- **AI 对话与补全**：`ChatController` 读取 `X-Endpoint` 头转发到指定模型；前端 `chat/`、`utils/` 负责对话、补全与编辑指令的调用。
- **NuGet 与文件管理**：`PackageSourceController`/`NugetProxyController` 提供包源与代理，前端 `components/nuget` 管理包引用；`fileSystem/` 支持本地存储、多目录、导入/导出。

## 前端要点
- 入口 `wwwroot/index.js` 组织编辑器、运行器、文件系统与通知；`editor/` 绑定 Monaco 状态与快捷键，`execution/` 管理运行与输出，`components/` 提供 UI 组件。
- 静态资源包含 `monaco-editor`、主题样式、Markdown、图标与语义着色定义。

## 构建与运行
- 依赖恢复：`dotnet restore SharpPad.sln`
- 编译：`dotnet build SharpPad.sln -c Debug`
- 启动 Web：`dotnet run --project SharpPad/SharpPad.csproj`
- 启动桌面：`dotnet run --project SharpPad.Desktop/SharpPad.Desktop.csproj`
- 容器体验：`docker compose up --build`（根目录）

## 状态与测试
- 当前未建单元测试项目，可按需新增 `SharpPad.Tests/*Tests.cs`（xUnit 建议）。
- 重要改动请至少执行一次 `dotnet build` 或相关端到端验证。
