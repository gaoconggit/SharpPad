# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SharpPad is a web-based C# code editor and execution platform similar to LinqPad, built with ASP.NET Core 9.0 and Monaco Editor. It combines Roslyn compiler services with modern web technologies to provide real-time code execution, IntelliSense, AI chat integration, and NuGet package management. The platform now includes both web and desktop versions.

## Architecture

### Three-Project Structure
- **SharpPad/**: Main ASP.NET Core 9.0 web application that serves the UI and provides API endpoints
- **MonacoRoslynCompletionProvider/**: .NET 9.0 library that handles all Roslyn-based code analysis, compilation, and IntelliSense features
- **SharpPad.Desktop/**: Avalonia-based desktop application that hosts the web UI in a WebView with native menus and system integration

### Key Components
- **Frontend**: Monaco Editor with custom C# language provider, responsive UI with resizable panels
- **Backend API**: RESTful endpoints for code execution, completion, chat proxy, and file management
- **Code Analysis Engine**: Roslyn-based workspace management with NuGet integration and dynamic assembly loading
- **AI Integration**: GPT chat and code completion with configurable models and endpoints

## Development Commands

### Building the Project
```bash
# Build entire solution
dotnet build SharpPad.sln

# Build specific projects
dotnet build SharpPad/SharpPad.csproj
dotnet build MonacoRoslynCompletionProvider/MonacoRoslynCompletionProvider.csproj

# Release build
dotnet build -c Release
```

### Running the Application

**Web Application:**
```bash
# Run web version in development mode
dotnet run --project SharpPad/SharpPad.csproj

# Run with specific environment
ASPNETCORE_ENVIRONMENT=Development dotnet run --project SharpPad/SharpPad.csproj
```

**Desktop Application:**
```bash
# Run desktop version
dotnet run --project SharpPad.Desktop/SharpPad.Desktop.csproj

# Build desktop releases
# Windows
.\build-desktop.ps1 -Configuration Release

# macOS/Linux
./build-desktop.sh --configuration Release
```

### Docker Development
```bash
# Build and run with Docker Compose
docker compose up -d

# Rebuild and restart
docker compose build sharppad && docker compose down && docker compose up -d

# Stop services
docker compose down
```

### Testing
```bash
# Run all tests (if test projects exist)
dotnet test

# Test specific project
dotnet test SharpPad/SharpPad.csproj
```

## Key API Endpoints

### Code Execution (`/api/coderun/`)
- Compiles and executes C# code with real-time output streaming
- Uses isolated `AssemblyLoadContext` for memory management
- Supports NuGet package integration during runtime

### Completion Services (`/completion/`)
- `/complete`: IntelliSense autocompletion
- `/signature`: Method signature help  
- `/hover`: Symbol information on hover
- `/codeCheck`: Syntax and semantic validation
- `/format`: Code formatting
- `/addPackages`: NuGet package management

### Chat Integration (`/v1/chat/`)
- Proxy for AI/GPT chat completion with streaming support
- Supports custom endpoints and authorization tokens
- Optimized for real-time responses with Server-Sent Events

## Frontend Architecture

### Core JavaScript Modules
- **editor/editor.js**: Monaco Editor integration with C# language support
- **fileSystem/fileManager.js**: File and folder management using localStorage
- **chat/chat.js**: AI chat interface with streaming responses
- **execution/runner.js**: Code execution interface
- **utils/apiService.js**: Centralized API communication

### UI Features
- Responsive design supporting desktop and mobile
- Resizable panels (editor, output, file list, chat)
- Context menus for file operations
- Theme switching (dark/light)
- Multi-model AI integration

## Desktop Application Architecture

### Key Components
- **WebServerManager**: Embedded ASP.NET Core server with automatic port assignment
- **MainWindow**: Avalonia XAML window with WebView integration and native menus
- **MainWindowViewModel**: MVVM pattern with commands for file operations
- **WebView Integration**: Uses `AvaloniaWebView.WebView` for rendering web content

### Desktop-Specific Features
- Native menu bar with keyboard shortcuts
- Embedded web server reads configuration from SharpPad's `appsettings.json`
- Automatic path resolution to SharpPad project's `wwwroot` directory
- Cross-platform support (Windows, macOS, Linux)

## NuGet Package System

### Package Management
- Packages downloaded asynchronously from nuget.org
- Cached locally in `/app/NugetPackages/packages/`
- Supports .NET 8.0, .NET Standard 2.0/2.1 frameworks
- Dynamic assembly loading with `AssemblyLoadContext`

### Performance Optimizations
- Concurrent package downloads
- Assembly reference caching with `ConcurrentDictionary`
- Workspace pooling and reuse
- Automatic garbage collection and cleanup

## Development Guidelines

The project follows specific coding standards defined in `.cursorrules`:

### Code Principles
- **Simplicity**: Write simple, direct code (fewer lines = less debt)
- **Readability**: Ensure code is easy to read and understand
- **Maintainability**: Write code that's easy to maintain and update
- **Performance**: Consider performance without over-optimization

### Coding Standards
- Use early returns to avoid nested conditions
- Prefer descriptive naming (event handlers prefixed with "handle")
- Use constants over functions where applicable
- Favor functional and immutable styles
- Minimize code changes - only modify relevant parts

### Function Organization
- Place composite functions before their components
- Add function comments describing purpose
- Use JSDoc for JavaScript (unless TypeScript)

## Configuration

### Environment Variables
- `ASPNETCORE_ENVIRONMENT`: Set to Development/Production
- `ASPNETCORE_URLS`: Default is http://+:5090

### Key Directories
- `wwwroot/`: Frontend assets and Monaco Editor files
- `NugetPackages/packages/`: NuGet package cache
- `KingOfTool/`: Example code snippets and configurations
- `SharpPad.Desktop/`: Desktop application project
- `Dll/`: Pre-loaded assemblies (FreeSql, CSRedisCore, RestSharp, etc.)

### Configuration Files
- `appsettings.json`: Web server configuration (Kestrel endpoints, logging)
- `NuGet.config`: Multiple NuGet sources (official, regional mirrors)
- `docker-compose.yml`: Container deployment with health checks
- `build-desktop.ps1/.sh`: Cross-platform desktop build scripts

## Docker Configuration

The application is containerized with:
- Multi-stage Docker build for optimization
- Volume persistence for NuGet packages
- Health checks and resource limits
- Proper permission handling for package directories

## Keyboard Shortcuts

### Editor Shortcuts
- `Ctrl + Enter`: Run code
- `Alt + C`: Clear output
- `Ctrl + J`: Code completion
- `Ctrl + K, Ctrl + D`: Format code
- `Ctrl + S`: Save code
- `Ctrl + Shift + Space`: Manual GPT auto-completion

### Chat Shortcuts
- `Alt + L`: Clear chat history (when cursor is in chat box)

## Important Notes

- File management is client-side using localStorage
- Code execution happens in isolated contexts for safety
- AI integration supports multiple providers and custom endpoints
- The application targets .NET 9.0 with C# Latest support
- Monaco Editor provides full IDE-like experience in the browser
- Desktop version embeds web server and uses WebView for rendering
- Configuration is centralized in SharpPad's `appsettings.json` for both web and desktop versions
- Both projects support hot-reload during development
- don't modify SharpPad.csproj To Web