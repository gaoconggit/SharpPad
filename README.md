**[English](README_EN.md)** | **简体中文**

# SharpPad
基于 Roslyn 、 monaco-editor 开发的快速验证 C# 代码片段的工具。类似 LinqPad,支持ai chat与ai completion

![image](https://github.com/user-attachments/assets/019f4b60-4d17-4629-aca1-1cddac5b15e1)

## PC版本体验更好,推荐使用PC版本,从0.2.0 版本起只提供PC版本

## 演示站点:

 - https://try.dotnet10.com  国内

## 快速体验
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
 #### 开发环境 建议 vs 2022 + net8.0
1. git clone 代码仓库
2. 在 Visual Studio 中启动 SharpPad 项目。
3. 使用 `Ctrl + Enter` 运行 "Hello World"。

## 快捷键

- `Ctrl + Enter` 运行代码
- `Alt + C` 清空输出信息
- `Ctrl + J` 代码提示
- `Ctrl + K` `Ctrl + D` 格式化代码
- `Ctrl + S` 保存代码
- `Alt+L` 光标在聊天框时,清空聊天记录
- `Ctrl + Shift + Space` 手动触发GPT自动补全

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

## 期待实现的功能

- 进行中的任务
  - nuget 管理页面
  - 多文件支持
  

## 项目致谢

 - 感谢 [monacopilot](https://github.com/arshad-yaseen/monacopilot) 提供的 GPT 自动完成功能。

##
[![Star History Chart](https://api.star-history.com/svg?repos=gaoconggit/SharpPad&type=Date)](https://star-history.com/#gaoconggit/SharpPad&Date)






