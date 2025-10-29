**English** | **[简体中文](README.md)**

# SharpPad

A lightweight tool for quickly validating C# code snippets, built with Roslyn and monaco-editor. Similar to LinqPad, with AI chat and AI completion support.

<img width="1917" height="1036" alt="image" src="https://github.com/user-attachments/assets/1a807014-844b-4ff0-b2dd-a81305101b3f" />


## PC Version Recommended
**The PC version provides a better experience. Starting from version 0.2.0, only the PC version is provided.**

### Quick Start
1. Download the Windows or macOS release package
2. Extract and run SharpPad.Desktop.exe
3. Open your browser and navigate to: http://localhost:5090
4. Enjoy!

## Demo Site
- https://try.dotnet10.com

### Pre-0.2.0 Quick Start (Legacy)
1. Download the release package
2. Extract and run SharpPad.exe
3. Open your browser and navigate to the ip:port shown in the console

## Docker Compose
In the project root directory:
1. Run new service: `docker compose up -d`
2. Update service: `docker compose build sharppad && docker compose down && docker compose up -d`
3. Stop service: `docker compose down`

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
- `Ctrl + Shift + K` - AI code editing
- `Ctrl + D` - Duplicate line

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
- C# version selection support
- Mobile-friendly UI
- Visual Studio breakpoint debugging integration (see [Debugger.md](https://github.com/gaoconggit/SharpPad/blob/main/KingOfTool/Debugger.md))
- Windows and macOS PC clients
- NuGet package management page
- Multi-file support
- WinForms application execution support

## Planned Features

- Work in Progress
  - Native breakpoint debugging
  - File import/export on macOS
 
## Project Acknowledgment

- Thanks to [monacopilot](https://github.com/arshad-yaseen/monacopilot) for GPT auto-completion functionality.

##
[![Star History Chart](https://api.star-history.com/svg?repos=gaoconggit/SharpPad&type=Date)](https://star-history.com/#gaoconggit/SharpPad&Date)


