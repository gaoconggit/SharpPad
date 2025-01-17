# SharpPad
基于 Roslyn 、 monaco-editor 开发的快速验证 C# 代码片段的小玩意。类似 LinqPad,支持ai chat

![image](https://github.com/user-attachments/assets/ab2d0f49-7974-4b3b-b7ce-c7455c485abf)



## 快速体验
1. 下载 release 包
2. 解压后,找到 SharpPad.exe 双击运行
3. 浏览器打开控制台显示的ip:port

## 示例代码参考
  https://github.com/gaoconggit/SharpPad/tree/main/KingOfTool
 ![image](https://github.com/user-attachments/assets/6df73f74-5f14-4f98-8842-3828b35e4580)
 ### 快速导入:
   1. [下载配置](https://github.com/gaoconggit/SharpPad/blob/main/KingOfTool/KingOfTool.json)
   2. 在sharpPad上创建一个目录
   3. 在目录中按右键,选择导入 KingOfTool.json
   
## 开发向导
 #### 开发环境 建议 vs 2022 + net9.0
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

## 特性

- 代码智能提示
- 支持导入 NuGet 包
- 多目录支持
- 导入导出目录功能
- ai chat

## 期待实现的功能

- 进行中的任务
  - ai auto completion
  - ai 生成的代码应用到编辑器
