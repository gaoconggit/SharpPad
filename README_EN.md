**English** | **[简体中文](README.md)**

# SharpPad

A lightweight tool for quickly validating C# code snippets, built with Roslyn and monaco-editor. Similar to LinqPad, with AI chat and AI completion support.

![image](https://github.com/user-attachments/assets/019f4b60-4d17-4629-aca1-1cddac5b15e1)

## Quick Start
1. Download the release package
2. Extract and run SharpPad.exe
3. Open your browser and navigate to the ip:port shown in the console

## Sample Code Reference
https://github.com/gaoconggit/SharpPad/tree/main/KingOfTool
![image](https://github.com/user-attachments/assets/6df73f74-5f14-4f98-8842-3828b35e4580)

### Quick Import:
1. [Download configuration](https://github.com/gaoconggit/SharpPad/blob/main/KingOfTool/KingOfTool.json)
2. Create a directory in SharpPad
3. Right-click in the directory and select import KingOfTool.json

## Development Guide
#### Development Environment: Recommended VS 2022 + .NET 9.0
1. Git clone the repository
2. Launch the SharpPad project in Visual Studio
3. Use `Ctrl + Enter` to run "Hello World"

## Keyboard Shortcuts

- `Ctrl + Enter` - Run code
- `Alt + C` - Clear output
- `Ctrl + J` - Code suggestions
- `Ctrl + K` `Ctrl + D` - Format code
- `Ctrl + S` - Save code
- `Alt + L` - Clear chat history (when cursor is in chat box)
- `Ctrl + Shift + Space` - Manually trigger GPT auto-completion

## Features

- Intelligent code suggestions
- NuGet package import support
- Multi-directory support
- Directory import/export functionality
- AI chat
- AI auto-completion
- Multiple model switching
- Theme switching
- Current directory only view

## Planned Features

- Work in Progress
  - Apply AI-generated code to editor
 
## Project Acknowledgment

- Thanks to [monacopilot](https://github.com/arshad-yaseen/monacopilot) for GPT auto-completion functionality.
