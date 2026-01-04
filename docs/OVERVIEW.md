# SharpPad Overview

A quick, ASCII-only summary of what the project does and how its pieces fit together.

## What SharpPad Does
- Lightweight Roslyn-powered C# scratchpad with a Monaco-based editor.
- Streamed code execution with multi-file and NuGet package support, plus optional exe build/download.
- AI helpers: chat, inline completion, and AI editing endpoints.
- File tree import/export, theme toggles, model switching, and mobile-friendly UI served from the same ASP.NET Core host.
- Desktop shells for Windows and macOS (Avalonia) that launch the local web experience on a free port.

## High-Level Flow
```
+---------------------------+
|   SharpPad.Desktop (UI)   |
| Avalonia shell + Monaco   |
+-------------+-------------+
              | opens http://localhost:{port}
              v
+-------------+-------------+
| SharpPad ASP.NET Core API |
| controllers: CodeRun,     |
| Chat, Completion, File    |
| Import, NuGet Proxy, etc. |
+-------------+-------------+
              | starts worker to run user code
              v
+---------------------------+
| SharpPad.ExecutionHost    |
| isolates assembly load,   |
| redirects console, runs   |
| user entry point safely   |
+---------------------------+
```

## Key Behaviors by Area
- Code execution: `CodeRunController` streams output/errors via SSE, supports stdin injection, stopping, multi-file builds, and download links for built artifacts.
- AI and editing: chat/completion/edit endpoints wrap MonacoRoslynCompletionProvider to offer model-backed assistance.
- Packages and files: NuGet proxy/source controllers manage package feeds; file import/export enables workspace sharing.
- Desktop startup: `InstanceManager` picks a free port per instance, sets `ASPNETCORE_URLS`, and boots the API + static assets the desktop shell points at.
