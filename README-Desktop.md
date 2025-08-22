# SharpPad Desktop

基于 Avalonia WebView 的 SharpPad 桌面版本，支持 Windows 和 macOS 平台。

## 特性

- 🖥️ 原生桌面应用体验
- 🌐 嵌入式 Web 服务器，无需额外配置
- 📱 响应式界面，支持窗口调整
- 🎨 原生菜单栏和快捷键支持
- 🔄 自动端口分配，避免冲突
- 💾 文件关联和系统集成

## 技术架构

- **Frontend**: Avalonia UI 11.x + WebView
- **Backend**: 嵌入式 ASP.NET Core 9.0
- **Runtime**: .NET 9.0

## 构建要求

- .NET 9.0 SDK
- Windows 10+ 或 macOS 10.15+
- Visual Studio 2022 或 JetBrains Rider (可选)

## 快速开始

### 构建项目

**Windows:**
```powershell
.\build-desktop.ps1 -Configuration Release
```

**macOS/Linux:**
```bash
./build-desktop.sh --configuration Release
```

### 运行开发版本

```bash
dotnet run --project SharpPad.Desktop/SharpPad.Desktop.csproj
```

## 项目结构

```
SharpPad.Desktop/
├── App.axaml                    # 应用程序入口
├── Program.cs                   # 主程序
├── Views/                       # UI 视图
│   └── MainWindow.axaml         # 主窗口
├── ViewModels/                  # 视图模型
│   └── MainWindowViewModel.cs   # 主窗口逻辑
├── Services/                    # 服务层
│   └── WebServerManager.cs     # Web服务管理
└── Assets/                      # 资源文件
    └── Styles.axaml            # 样式定义
```

## 开发指南

### 添加新功能

1. 在 `Services/` 中创建服务类
2. 在 `ViewModels/` 中添加业务逻辑
3. 在 `Views/` 中更新 UI
4. 更新 `MainWindowViewModel` 中的命令绑定

### 调试

- 使用 Visual Studio 或 Rider 的调试器
- WebView 内容可通过开发者工具调试
- 后端 API 日志输出到控制台

## 部署

### Windows 部署

生成独立可执行文件：
```powershell
dotnet publish SharpPad.Desktop/SharpPad.Desktop.csproj -c Release -r win-x64 --self-contained
```

### macOS 部署

生成 macOS 应用包：
```bash
dotnet publish SharpPad.Desktop/SharpPad.Desktop.csproj -c Release -r osx-x64 --self-contained
```

## 已知限制

- WebView 需要系统 WebView2 支持 (Windows)
- macOS 需要 10.15+ 系统版本
- 首次启动可能需要较长时间来初始化

## 故障排除

### 常见问题

1. **WebView 无法加载**: 检查 WebView2 运行时是否已安装
2. **端口占用**: 应用会自动选择可用端口
3. **权限问题**: 确保应用有网络访问权限

### 日志查看

应用日志输出到控制台，可通过以下方式查看：
```bash
dotnet run --project SharpPad.Desktop/SharpPad.Desktop.csproj --verbosity detailed
```

## 贡献指南

1. Fork 项目
2. 创建特性分支 (`git checkout -b feature/amazing-feature`)
3. 提交更改 (`git commit -m 'Add amazing feature'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 创建 Pull Request

## 许可证

本项目与 SharpPad 主项目使用相同的许可证。