# Repository Guidelines

## Project Structure & Module Organization
SharpPad.sln orchestrates three active projects: `SharpPad/` hosts the ASP.NET Core web app (controllers, services, DTOs, and `/wwwroot` assets), `SharpPad.Desktop/` provides the desktop shell, and `MonacoRoslynCompletionProvider/` supplies Roslyn-based language services. The root `Program.cs` ships utility entry points, while `SharpPad/Program.cs` boots the web host. Asset bundles, Monaco artifacts, and static pages live under `SharpPad/wwwroot`. No dedicated `Tests/` folder exists yet; place experimental tooling inside project-specific `Services/` or feature folders.

## Build, Test, and Development Commands
Run `dotnet restore SharpPad.sln` before first builds to fetch NuGet dependencies. Compile everything with `dotnet build SharpPad.sln -c Debug`. Launch the browser experience via `dotnet run --project SharpPad/SharpPad.csproj`, or start the desktop host with `dotnet run --project SharpPad.Desktop/SharpPad.Desktop.csproj`. For EF migrations, call `dotnet tool restore` inside `SharpPad/` and then invoke `dotnet ef ...`. Container workflows rely on `docker-compose up --build` when required.

## Coding Style & Naming Conventions
Use four-space indentation throughout C#. Favor `PascalCase` for namespaces, classes, and public members; `camelCase` for locals and parameters; `_camelCase` for private fields. Keep files and feature folders in English, e.g., `Services/CodeExecution/`. Prefer async APIs in controllers and services, and route complex logic into dedicated service classes instead of controllers.

## Testing Guidelines
Adopt xUnit when adding coverage. New fixtures should land in a `SharpPad.Tests` project with filenames ending in `*Tests.cs`. Execute the suite from the solution root with `dotnet test`. Request meaningful coverage for each behavioral change and include regression tests for bug fixes where feasible.

## Commit & Pull Request Guidelines
Commits follow a conventional prefix such as `feat`, `fix`, `refactor`, or `style`, coupled with a short imperative subject (e.g., `feat: add compiler diagnostics panel`). Reference issues with `#123` when applicable. Pull requests should summarize intent, describe validation steps, attach UI screenshots for notable visual changes, and link related issues. Keep diffs focused and ensure CI or local builds succeed before requesting review.

## Security & Configuration Tips
Never commit secrets or production settings. Keep local overrides inside `SharpPad/appsettings.Development.json` and lean on environment variables or secret stores in other environments. Sanitize and validate incoming data in controllers, and avoid logging sensitive payloads while still capturing actionable diagnostics.
