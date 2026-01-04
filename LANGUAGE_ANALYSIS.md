# Language Analysis

- Approach: counted tracked text lines by file extension using `git ls-files` piped into a small Python script, ignoring common binary formats.
- Result: JavaScript is the dominant language in this repository by line count, with C# as the next largest.

| Language   | Approx. lines |
|------------|---------------|
| JavaScript | 68,978        |
| C#         | 14,023        |
| CSS        | 5,765         |
| Markdown   | 4,407         |
| HTML       | 507           |
| JSON       | 428           |
| Shell      | 399           |
| MSBuild    | 280           |
| YAML       | 145           |
| PowerShell | 34            |
| Config     | 9             |

JavaScript leads largely due to the bundled front-end assets (for example, under `SharpPad/wwwroot`) that power the editor experience, while the host application and tooling are primarily written in C#.
