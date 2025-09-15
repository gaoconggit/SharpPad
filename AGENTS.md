# Repository Guidelines

## Project Structure & Module Organization
- Root solution: SharpPad.sln with projects:
  - SharpPad (ASP.NET Core Web app: Controllers/, Services/, Dto/, static assets in wwwroot/).
  - SharpPad.Desktop (desktop host/integration).
  - MonacoRoslynCompletionProvider (language/completion services).
- Entry points: root Program.cs (utility) and SharpPad/Program.cs (web).
- Assets: SharpPad/wwwroot (js, styles, monaco editor, pages).
- No dedicated 	ests/ folder currently.

## Build, Test, and Development Commands
- Restore: dotnet restore SharpPad.sln — restore NuGet packages.
- Build (Debug): dotnet build SharpPad.sln -c Debug — compile all projects.
- Run Web: dotnet run --project SharpPad/SharpPad.csproj — start API/UI (defaults from launchSettings.json).
- Run Desktop: dotnet run --project SharpPad.Desktop/SharpPad.Desktop.csproj — start desktop shell.
- EF tooling: from SharpPad/.config/dotnet-tools.json use dotnet tool restore, then dotnet ef ....
- Docker (optional): docker-compose up --build — build and run containerized services if used.

## Coding Style & Naming Conventions
- C#: 4-space indentation, PascalCase for classes/namespaces, camelCase for locals/parameters, _camelCase for private fields.
- Files/folders in English; keep feature-based folders inside SharpPad (e.g., Services/CodeExecution/).
- Prefer async APIs; avoid blocking calls on UI/web paths.
- Keep controllers thin; move logic into Services.

## Testing Guidelines
- No formal test project detected. If adding tests, prefer xUnit with project SharpPad.Tests and files named *Tests.cs.
- Run tests: dotnet test at solution root.
- Target meaningful coverage for new/changed code.

## Commit & Pull Request Guidelines
- Commit style observed: conventional prefixes (feat, fix, refactor, style, merge/update) and short imperative subject; add scope when useful.
- Reference issues with #123 where applicable; use English where possible.
- PRs: include summary, screenshots for UI changes, steps to validate, and link related issues. Keep diffs focused.

## Security & Configuration Tips
- Do not commit secrets. Use ppsettings.Development.json locally and environment variables in production.
- Validate and sanitize inputs in controllers; log errors with context, avoid sensitive data in logs.