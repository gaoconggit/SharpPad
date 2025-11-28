**[English](README_EN.md)** | **简体中文**

# SharpPad
基于 Roslyn 、 monaco-editor 开发的快速验证 C# 代码片段的工具。类似 LinqPad,支持ai chat、ai completion、ai edit

<img width="1917" height="1036" alt="image" src="https://github.com/user-attachments/assets/69cf6124-e3e1-4753-98da-0ee9cce80e5d" />



## PC版本体验更好,推荐使用PC版本,从0.2.0 版本起只提供PC版本
 ### 快速体验
   1. 下载windows或mac release 包
   2. 解压后，运行 SharpPad.Desktop.exe
   3. 保留浏览器访问: http://localhost:5090
   4. Enjoy !

## 演示站点:

 - https://try.dotnet10.com (老版本)
## 0.2.0 版本前快速体验
1. 下载 release 包
2. 解压后,找到 SharpPad.exe 双击运行
3. 浏览器打开控制台显示的ip:port

## Docker Compose
  在项目根目录
   1. 跑新服务 `docker compose up -d`
   2. 更新服务 `docker compose build sharppad && docker compose down && docker compose up -d `
   3. 停止服务 `docker compose down`
   

## 示例代码参考
  https://github.com/gaoconggit/SharpPad/tree/main/KingOfTool
 ![image](https://github.com/user-attachments/assets/6df73f74-5f14-4f98-8842-3828b35e4580)
 ### 快速导入:
   1. [下载配置](https://github.com/gaoconggit/SharpPad/blob/main/KingOfTool/KingOfTool.json)
   2. 在sharpPad上创建一个目录
   3. 在目录中按右键,选择导入 KingOfTool.json
   
## 开发向导
 #### 开发环境 建议 VS 2022 + .NET 10 SDK
1. git clone 代码仓库
2. 在 Visual Studio 中启动 SharpPad 项目。
3. 使用 `Ctrl + Enter` 运行 "Hello World"。

## 快捷键
# win
- `Ctrl + Enter` 运行代码
- `Alt + C` 清空输出信息
- `Ctrl + J` 代码提示
- `Ctrl + K` `Ctrl + D` 格式化代码
- `Ctrl + S` 保存代码
- `Alt+L` 光标在聊天框时,清空聊天记录
- `Ctrl + Shift + Space` 手动触发GPT自动补全
- `Ctrl + Shift + K` AI代码编辑
- `Ctrl + D` 复制上一行到下一行

# mac

- `Cmd + Enter` 运行代码
- `Ctrl + Option + C` 清空输出信息
- `Cmd + J` 代码提示
- `Cmd + K` `Cmd + D` 格式化代码
- `Cmd + S` 保存代码
- `Cmd + Shift + Space` 手动触发GPT自动补全
- `Cmd + Shift + K` AI代码编辑
- `Cmd + D` 复制上一行到下一行

## 特性

- 代码智能提示
- 支持导入 NuGet 包
- 多目录支持
- 导入导出目录功能
- ai chat
- ai auto completion
- 多模型切换
- 主题切换
- 仅显示当前目录
- 支持选择c#版本
- 移动端UI
- 启动VS断点调试(参考 https://github.com/gaoconggit/SharpPad/blob/main/KingOfTool/Debugger.md)
- windows pc client(mac silicon / windows)
- nuget 管理页面
- 多文件支持
- 支持运行winform
- 支持运行web api
- 支持运行 avalonia

## 期待实现的功能

- 进行中的任务
  - 原生断点调试

    
  

## 项目致谢

 - 感谢 [monacopilot](https://github.com/arshad-yaseen/monacopilot) 提供的 GPT 自动完成功能。

##
[![Star History Chart](https://api.star-history.com/svg?repos=gaoconggit/SharpPad&type=Date)](https://star-history.com/#gaoconggit/SharpPad&Date)




























