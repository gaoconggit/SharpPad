Project Language Summary
========================

Methodology
-----------
- Counted tracked files by extension and summed line counts (text decode errors ignored).
- Mapped common extensions to languages; ignored unknown extensions and binaries.

Results
-------
- JavaScript: 68,978 lines (~77.4%) across 124 files
- C#: 14,023 lines (~15.7%) across 74 files
- Markdown: 4,816 lines (~5.4%) across 44 files
- JSON: 428 lines (~0.5%) across 9 files
- Shell: 399 lines (~0.4%) across 1 file
- MSBuild (sln/csproj/props/targets): 280 lines (~0.3%) across 5 files
- YAML: 145 lines (~0.2%) across 3 files
- Dockerfile: 39 lines (~0.0%) across 1 file
- PowerShell: 34 lines (~0.0%) across 1 file
- XML: 9 lines (~0.0%) across 1 file

Notes
-----
- JavaScript dominates due to the `SharpPad/wwwroot` frontend assets (includes bundled/minified libraries).
- C# covers the application logic in `SharpPad`, `SharpPad.Desktop`, and `SharpPad.ExecutionHost`.
