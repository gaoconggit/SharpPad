# SharpPad 项目总览

## 项目简介

SharpPad 是一个基于 Roslyn 和 Monaco Editor 的 C# 代码实验平台，提供类似 LinqPad 的快速代码验证体验。项目支持 AI 辅助功能（聊天、自动补全、代码编辑），可在浏览器或桌面端运行，为 C# 开发者提供即时编译、智能提示与交互式执行环境。

## 核心特性

- **代码智能提示** - 基于 Roslyn 的 IntelliSense，提供补全、签名帮助、悬停信息
- **NuGet 包管理** - 支持在线导入和管理 NuGet 包
- **多文件支持** - 可创建和运行多文件 C# 项目
- **AI 集成** - 支持 AI 聊天、自动补全和代码编辑
- **多模型切换** - 可配置多个 AI 模型端点
- **跨平台部署** - Web 应用、桌面应用和 Docker 容器
- **文件系统管理** - 多目录支持、导入导出功能
- **主题切换** - 支持明暗主题
- **多项目类型** - 支持控制台、WinForms、Web API 和 Avalonia 应用

## 解决方案结构

```
SharpPad.sln
├── SharpPad/                          # ASP.NET Core Web 应用（主项目）
├── SharpPad.Desktop/                  # Avalonia 跨平台桌面应用
├── MonacoRoslynCompletionProvider/    # Roslyn 语言服务库
├── SharpPad.ExecutionHost/            # 代码执行宿主
└── KingOfTool/                        # 示例脚本和配置集合
```

## 项目详解

### SharpPad（Web 应用）

主要的 ASP.NET Core 9.0 Web 应用，提供 API 端点和静态资源托管。

#### 后端架构

| 组件 | 职责 |
|------|------|
| `Controllers/CompletionController` | 代码补全、签名提示、Hover、格式化、语义 Token、代码诊断 |
| `Controllers/CodeRunController` | 代码执行、SSE 流式输出、多文件运行、可执行文件构建 |
| `Controllers/ChatController` | AI 对话代理，支持自定义端点和流式响应 |
| `Controllers/NugetProxyController` | NuGet 包代理服务 |
| `Controllers/FileImportController` | 文件导入功能 |
| `Services/InstanceManager` | 多实例端口管理，默认端口 5090 |

#### 前端架构

```
wwwroot/
├── index.js                 # 应用入口，初始化 Monaco 和各模块
├── editor/                  # 编辑器状态与命令管理
│   ├── editor.js           # Monaco Editor 封装
│   └── commands.js         # 快捷键命令
├── execution/               # 代码执行模块
│   ├── runner.js           # 代码运行器
│   └── outputPanel.js      # 输出面板
├── fileSystem/              # 文件系统管理
│   └── fileManager.js      # 本地存储、导入导出
├── components/              # UI 组件
│   └── nuget/              # NuGet 包管理界面
├── chat/                    # AI 聊天界面
├── utils/                   # 公共工具
│   ├── apiService.js       # API 调用封装
│   └── desktopBridge.js    # 桌面应用通信桥
├── csharpLanguageProvider.js # C# 语言提供器
├── semanticColoring.js      # 语义着色
└── monaco-editor/           # Monaco Editor 资源
```

#### API 端点

| 端点 | 方法 | 功能 |
|------|------|------|
| `/completion/complete` | POST | 代码补全 |
| `/completion/signature` | POST | 方法签名帮助 |
| `/completion/hover` | POST | 悬停信息 |
| `/completion/codeCheck` | POST | 代码诊断 |
| `/completion/format` | POST | 代码格式化 |
| `/completion/definition` | POST | 跳转定义 |
| `/completion/semanticTokens` | POST | 语义 Token |
| `/completion/addPackages` | POST | 添加 NuGet 包 |
| `/api/coderun/run` | POST | 运行代码（SSE 流） |
| `/api/coderun/stop` | POST | 停止执行 |
| `/api/coderun/input` | POST | 提供用户输入 |
| `/api/coderun/buildExe` | POST | 构建可执行文件 |
| `/v1/chat/completions` | POST | AI 对话代理 |

### SharpPad.Desktop（桌面应用）

基于 Avalonia 11 的跨平台桌面应用，嵌入 WebView 并复用 Web 项目。

#### 核心组件

| 组件 | 职责 |
|------|------|
| `Views/MainWindow` | 主窗口，包含 WebView 和原生菜单 |
| `Services/WebServerManager` | 嵌入式 ASP.NET Core 服务器管理 |
| `ViewModels/MainWindowViewModel` | MVVM 视图模型 |
| `Interop/` | 平台互操作功能 |

#### 技术栈

- Avalonia 11.3.9
- WebView.Avalonia.Cross 11.3.1
- 目标框架：Windows 使用 net9.0-windows，其他平台使用 net9.0

### MonacoRoslynCompletionProvider（语言服务库）

Roslyn 驱动的语言服务库，提供完整的 C# IDE 功能。

#### 核心类

| 类 | 职责 |
|------|------|
| `MonacoRequestHandler` | 请求处理入口，协调各 Provider |
| `CompletionWorkspace` | Roslyn 工作区管理，引用缓存 |
| `CompletionDocument` | 文档级别的语言服务 |
| `TabCompletionProvider` | 代码补全提供器 |
| `SignatureHelpProvider` | 方法签名帮助 |
| `HoverInformationProvider` | 悬停信息提供器 |
| `DefinitionProvider` | 跳转定义功能 |
| `SemanticTokensProvider` | 语义 Token 提供器 |
| `CodeCheckProvider` | 代码诊断提供器 |
| `CodeActionProvider` | 代码操作（Quick Fix）|
| `CodeRunner` | 代码编译和执行 |

#### 依赖

- Microsoft.CodeAnalysis 4.14.0（Roslyn）
- NuGet.Protocol 6.10.0
- IgnoresAccessChecksToGenerator（访问内部 API）

### SharpPad.ExecutionHost（执行宿主）

独立的代码执行进程，用于隔离用户代码执行。

## 构建与运行

### 环境要求

- .NET 9.0 SDK
- Visual Studio 2022（推荐）或其他 IDE

### 构建命令

```bash
# 恢复依赖
dotnet restore SharpPad.sln

# 编译解决方案
dotnet build SharpPad.sln -c Debug

# 发布版本
dotnet build SharpPad.sln -c Release
```

### 运行应用

```bash
# Web 应用
dotnet run --project SharpPad/SharpPad.csproj

# 桌面应用
dotnet run --project SharpPad.Desktop/SharpPad.Desktop.csproj
```

### Docker 部署

```bash
# 启动服务
docker compose up -d

# 更新服务
docker compose build sharppad && docker compose down && docker compose up -d

# 停止服务
docker compose down
```

## 快捷键

### Windows

| 快捷键 | 功能 |
|--------|------|
| `Ctrl + Enter` | 运行代码 |
| `Alt + C` | 清空输出 |
| `Ctrl + J` | 代码提示 |
| `Ctrl + K, Ctrl + D` | 格式化代码 |
| `Ctrl + S` | 保存代码 |
| `Ctrl + Shift + Space` | 手动触发 AI 补全 |
| `Ctrl + Shift + K` | AI 代码编辑 |
| `Ctrl + D` | 复制上一行 |
| `Alt + L` | 清空聊天记录 |

### macOS

| 快捷键 | 功能 |
|--------|------|
| `Cmd + Enter` | 运行代码 |
| `Ctrl + Option + C` | 清空输出 |
| `Cmd + J` | 代码提示 |
| `Cmd + K, Cmd + D` | 格式化代码 |
| `Cmd + S` | 保存代码 |
| `Cmd + Shift + Space` | 手动触发 AI 补全 |
| `Cmd + Shift + K` | AI 代码编辑 |
| `Cmd + D` | 复制上一行 |

## 配置文件

| 文件 | 用途 |
|------|------|
| `SharpPad/appsettings.json` | Web 服务器配置（Kestrel、日志等）|
| `NuGet.config` | NuGet 源配置（官方源和镜像）|
| `docker-compose.yml` | Docker 容器编排 |
| `Dockerfile` | Docker 镜像构建 |

## 目录说明

| 目录 | 说明 |
|------|------|
| `SharpPad/wwwroot/` | 前端静态资源 |
| `SharpPad/Dll/` | 预加载的程序集 |
| `SharpPad/NugetPackages/` | NuGet 包缓存目录 |
| `KingOfTool/` | 示例脚本和配置 |
| `SharpPad.Desktop/Assets/` | 桌面应用资源 |

## 开发规范

### 代码风格

- C# 使用四空格缩进
- 类型与公共成员使用 PascalCase
- 参数与局部变量使用 camelCase
- 私有字段使用 `_camelCase`
- 控制器保持精简，复杂逻辑放入服务层

### 最佳实践

- 优先使用异步 API
- 新功能复用现有服务和模块
- 跨端共用逻辑放在 `SharpPad` 项目内
- 避免在代码中硬编码敏感配置

## 技术栈总览

### 后端

- ASP.NET Core 9.0
- Roslyn (Microsoft.CodeAnalysis) 4.14.0
- Newtonsoft.Json
- Swashbuckle (Swagger)

### 前端

- Monaco Editor
- 原生 JavaScript (ES Modules)
- CSS3 (响应式设计)

### 桌面

- Avalonia 11.3.9
- WebView.Avalonia

### DevOps

- Docker & Docker Compose
- GitHub Actions

## 相关文档

- [README.md](README.md) - 中文快速入门
- [README_EN.md](README_EN.md) - English Quick Start
- [CLAUDE.md](CLAUDE.md) - AI 开发助手指南
- [AGENTS.md](AGENTS.md) - 代理配置说明
- [KingOfTool/](KingOfTool/) - 示例代码和配置

## 致谢

- [monacopilot](https://github.com/arshad-yaseen/monacopilot) - GPT 自动完成功能
- [Roslyn](https://github.com/dotnet/roslyn) - C# 编译器和语言服务
- [Monaco Editor](https://github.com/microsoft/monaco-editor) - 代码编辑器
- [Avalonia](https://github.com/AvaloniaUI/Avalonia) - 跨平台 UI 框架
