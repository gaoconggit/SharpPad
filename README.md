# SharpPad
基于 Roslyn 和 monaco-editor 开发的快速验证 C# 代码片段的工具。

![SharpPad](https://github.com/user-attachments/assets/86302f6f-7a0c-4e75-913d-2c725b3dc1c8)

## 快速体验
1. 下载 release 包
2. 解压后,找到 SharpPad.exe 双击运行
3. 浏览器打开控制台显示的ip:port
   
## 开发向导
 #### 开发环境 建议 vs 2022 + net8.0
1. git clone 代码仓库 
2. 进入 `wwwroot` 目录安装 monaco-editor：
    ```bash
    npm i --registry=https://registry.npm.taobao.org
    ```
3. 在 Visual Studio 中启动 SharpPad 项目。
4. 使用 `Ctrl + Enter` 运行 "Hello World"。


## 示例代码参考
  https://github.com/gaoconggit/SharpPad/tree/main/KingOfTool
  ![image](https://github.com/user-attachments/assets/898a124c-bde9-4e6e-89c3-8e8f9f39266b)


## 快捷键

- `Ctrl + Enter` 运行代码
- `Alt + C` 清空输出信息
- `Ctrl + J` 代码提示
- `Ctrl + K` `Ctrl + D` 格式化代码
- `Ctrl + S` 保存代码

## 特性

- 代码智能提示
- 支持导入 NuGet 包
- 多目录支持
- 导入导出目录功能
- ai chat

## 期待实现的功能

- 进行中的任务
  - ai auto completion
