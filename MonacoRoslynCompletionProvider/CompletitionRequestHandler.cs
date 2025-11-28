using System.Composition.Hosting;
using System.Linq;
using MonacoRoslynCompletionProvider.Api;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using System;
using System.Collections.Generic;
using monacoEditorCSharp.DataHelpers;
using System.IO;

namespace MonacoRoslynCompletionProvider
{
    public static class MonacoRequestHandler
    {
        private static string[] GetAssembliesForProjectType(string projectType = null)
        {
            var baseAssemblies = new List<string>();

            // 添加应用程序域中已加载的程序集
            baseAssemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location));

            // 只有在项目类型为 winforms 时才加载 Windows Forms 程序集
            if (IsWindowsFormsProject(projectType))
            {
                baseAssemblies.AddRange(GetWindowsFormsAssemblies());
            }

            return baseAssemblies.ToArray();
        }

        private static bool IsWindowsFormsProject(string projectType)
        {
            if (string.IsNullOrWhiteSpace(projectType))
            {
                return false;
            }

            var normalized = projectType.ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "");

            return normalized.Contains("winform") ||
                   normalized.Contains("form") ||
                   normalized.Contains("windows");
        }

        private static string[] GetWindowsFormsAssemblies()
        {
            if (!OperatingSystem.IsWindows())
            {
                return Array.Empty<string>();
            }

            var assemblies = new List<string>();
            var requiredAssemblies = new[]
            {
                "System.Windows.Forms",
                "System.Drawing",
                "System.Drawing.Common",
                "Microsoft.Win32.SystemEvents"
            };

            foreach (var assemblyName in requiredAssemblies)
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    if (!string.IsNullOrEmpty(assembly.Location))
                    {
                        assemblies.Add(assembly.Location);
                    }
                }
                catch
                {
                    // 忽略加载失败的程序集
                }
            }

            return assemblies.ToArray();
        }

        public static async Task<TabCompletionResult[]> CompletionHandle(TabCompletionRequest tabCompletionRequest, string nuget, string projectType = null)
        {
            // 加载 NuGet 包
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);
            var code = tabCompletionRequest.Code ?? string.Empty;

            var document = await workspace.CreateDocumentAsync(code);
            return await document.GetTabCompletion(tabCompletionRequest.Position, CancellationToken.None);
        }


        public static async Task<HoverInfoResult> HoverHandle(HoverInfoRequest hoverInfoRequest, string nuget, string projectType = null)
        {
            // 加载 NuGet 包
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);

            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);
            var code = hoverInfoRequest.Code ?? string.Empty;

            var document = await workspace.CreateDocumentAsync(code);
            return await document.GetHoverInformation(hoverInfoRequest.Position, CancellationToken.None);
        }


        public static async Task<CodeCheckResult[]> CodeCheckHandle(CodeCheckRequest codeCheckRequest, string nuget, string projectType = null)
        {
            // 加载 NuGet 包
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);

            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);
            var code = codeCheckRequest.Code ?? string.Empty;

            var document = await workspace.CreateDocumentAsync(code);
            return await document.GetCodeCheckResults(CancellationToken.None);
        }


        public static async Task<SignatureHelpResult> SignatureHelpHandle(SignatureHelpRequest signatureHelpRequest, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);
            var code = signatureHelpRequest.Code ?? string.Empty;

            var document = await workspace.CreateDocumentAsync(code);
            return await document.GetSignatureHelp(signatureHelpRequest.Position, CancellationToken.None);
        }


        public static async Task<DefinitionResult> DefinitionHandle(DefinitionRequest definitionRequest, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);
            var code = definitionRequest.Code ?? string.Empty;

            var document = await workspace.CreateDocumentAsync(code);
            return await document.GetDefinition(definitionRequest.Position, CancellationToken.None);
        }


        public static async Task<SemanticTokensResult> SemanticTokensHandle(SemanticTokensRequest semanticTokensRequest, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);
            var code = semanticTokensRequest.Code ?? string.Empty;

            var document = await workspace.CreateDocumentAsync(code);
            return await document.GetSemanticTokens(CancellationToken.None);
        }


        public static async Task<CodeActionResult[]> CodeActionsHandle(CodeActionRequest codeActionRequest, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);
            var code = codeActionRequest.Code ?? string.Empty;

            var document = await workspace.CreateDocumentAsync(code);
            return await document.GetCodeActions(
                codeActionRequest.Position,
                codeActionRequest.SelectionStart,
                codeActionRequest.SelectionEnd,
                CancellationToken.None);
        }


        //格式化代码
        public static string FormatCode(string sourceCode)
        {
            var assemblies = new[]
            {
                Assembly.Load("Microsoft.CodeAnalysis"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp"),
                Assembly.Load("Microsoft.CodeAnalysis.Features"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"),
            };

            var partTypes = MefHostServices.DefaultAssemblies.Concat(assemblies)
                .Distinct()
                .SelectMany(x => x.GetTypes())
                .ToArray();

            var compositionContext = new ContainerConfiguration()
                .WithParts(partTypes)
                .CreateContainer();

            var host = MefHostServices.Create(compositionContext);

            var workspace = new AdhocWorkspace(host);
            var sourceLanguage = new CSharpLanguage();

            SyntaxTree syntaxTree = sourceLanguage.ParseText(sourceCode, SourceCodeKind.Script);
            var root = (CompilationUnitSyntax)syntaxTree.GetRoot();
            return Formatter.Format(root, workspace).ToFullString();
        }

        // Multi-file completion methods
        public static async Task<TabCompletionResult[]> MultiFileCompletionHandle(MultiFileTabCompletionRequest request, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);

            var files = request.Files?.ToList() ?? new List<FileContent>();

            var targetFile = files.FirstOrDefault(f => f.FileName == request.TargetFileId)
                          ?? files.FirstOrDefault();

            if (targetFile == null)
            {
                return Array.Empty<TabCompletionResult>();
            }

            // Create a combined document with all files for better context
            var combinedCode = CreateCombinedCode(files, targetFile.FileName);
            var document = await workspace.CreateDocumentAsync(combinedCode);

            // Adjust position to account for the combined code structure
            var adjustedPosition = GetAdjustedPosition(files, targetFile.FileName, request.Position);

            return await document.GetTabCompletion(adjustedPosition, CancellationToken.None);
        }


        public static async Task<CodeCheckResult[]> MultiFileCodeCheckHandle(MultiFileCodeCheckRequest request, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);

            var files = request.Files?.ToList() ?? new List<FileContent>();

            // Create a combined document with all files; ensure target file (if provided)
            // is included as-is so offsets map cleanly back to the original editor content.
            var combinedCode = CreateCombinedCode(files, request.TargetFileId);
            var document = await workspace.CreateDocumentAsync(combinedCode);

            var results = await document.GetCodeCheckResults(CancellationToken.None);

            // Map diagnostics back to the active file offsets when in multi-file mode
            return AdjustCodeCheckResults(results, files, request.TargetFileId, combinedCode);
        }


        public static async Task<SemanticTokensResult> MultiFileSemanticTokensHandle(MultiFileSemanticTokensRequest request, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);

            var files = request.Files?.ToList() ?? new List<FileContent>();

            var targetFile = files.FirstOrDefault(f => f.FileName == request.TargetFileId)
                          ?? files.FirstOrDefault();

            if (targetFile == null)
            {
                return new SemanticTokensResult();
            }

            var combinedCode = CreateCombinedCode(files, targetFile.FileName);
            var document = await workspace.CreateDocumentAsync(combinedCode);

            var tokensResult = await document.GetSemanticTokens(CancellationToken.None);
            if (tokensResult?.Data == null || tokensResult.Data.Count == 0)
            {
                return tokensResult ?? new SemanticTokensResult();
            }

            var filteredTokens = MapTokensToTargetFile(tokensResult.Data, files, targetFile.FileName, combinedCode);
            if (filteredTokens.Count == 0)
            {
                return new SemanticTokensResult();
            }

            var encodedTokens = EncodeSemanticTokens(filteredTokens);
            return new SemanticTokensResult { Data = encodedTokens };
        }


        public static async Task<HoverInfoResult> MultiFileHoverHandle(MultiFileHoverInfoRequest request, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);

            var files = request.Files?.ToList() ?? new List<FileContent>();

            var targetFile = files.FirstOrDefault(f => f.FileName == request.TargetFileId)
                          ?? files.FirstOrDefault();

            if (targetFile == null)
            {
                return new HoverInfoResult();
            }

            var combinedCode = CreateCombinedCode(files, targetFile.FileName);
            var document = await workspace.CreateDocumentAsync(combinedCode);

            var adjustedPosition = GetAdjustedPosition(files, targetFile.FileName, request.Position);

            return await document.GetHoverInformation(adjustedPosition, CancellationToken.None);
        }


        public static async Task<SignatureHelpResult> MultiFileSignatureHelpHandle(MultiFileSignatureHelpRequest request, string nuget, string projectType = null)
        {
            var nugetAssembliesArray = DownloadNugetPackages.LoadPackages(nuget).Select(a => a.Path).ToArray();
            var assemblies = GetAssembliesForProjectType(projectType);
            var workspace = await CompletionWorkspace.CreateAsync([.. nugetAssembliesArray, .. assemblies]);

            var files = request.Files?.ToList() ?? new List<FileContent>();

            var targetFile = files.FirstOrDefault(f => f.FileName == request.TargetFileId)
                          ?? files.FirstOrDefault();

            if (targetFile == null)
            {
                return new SignatureHelpResult();
            }

            var combinedCode = CreateCombinedCode(files, targetFile.FileName);
            var document = await workspace.CreateDocumentAsync(combinedCode);

            var adjustedPosition = GetAdjustedPosition(files, targetFile.FileName, request.Position);

            return await document.GetSignatureHelp(adjustedPosition, CancellationToken.None);
        }


        // Helper methods for multi-file support
        private static string CreateCombinedCode(List<FileContent> files, string targetFileName = null)
        {
            var combinedCode = new System.Text.StringBuilder();

            // Add using statements from all files first
            var allUsingStatements = new HashSet<string>();
            foreach (var file in files)
            {
                var lines = file.Content.Split('\n');
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("using ") && trimmedLine.EndsWith(";"))
                    {
                        allUsingStatements.Add(trimmedLine);
                    }
                }
            }

            // Add all unique using statements
            foreach (var usingStmt in allUsingStatements.OrderBy(u => u))
            {
                combinedCode.AppendLine(usingStmt);
            }

            combinedCode.AppendLine();

            // Append each file's content directly (without artificial namespaces)
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file.FileName);
                var sanitizedName = System.Text.RegularExpressions.Regex.Replace(fileName, @"[^\w]", "_");

                combinedCode.AppendLine($"// File: {file.FileName}");

                // Remove using statements from individual files
                var cleanContent = RemoveUsingStatements(file.Content);

                // Always include as-is (without extra wrappers), we already deduped usings.
                combinedCode.AppendLine(cleanContent);

                combinedCode.AppendLine();
            }

            return combinedCode.ToString();
        }

        private static string RemoveUsingStatements(string code)
        {
            var lines = code.Split('\n');
            var result = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("using ") || !trimmedLine.EndsWith(";"))
                {
                    result.Add(line);
                }
            }

            return string.Join("\n", result);
        }

        private static List<SemanticToken> MapTokensToTargetFile(List<int> encodedTokens, List<FileContent> files, string targetFileName, string combinedCode)
        {
            if (encodedTokens == null || encodedTokens.Count == 0 || files == null || files.Count == 0)
            {
                return new List<SemanticToken>();
            }

            var decodedTokens = DecodeSemanticTokens(encodedTokens);
            if (decodedTokens.Count == 0)
            {
                return decodedTokens;
            }

            var targetFile = files.FirstOrDefault(f => f.FileName == targetFileName) ?? files.First();
            if (targetFile == null)
            {
                return new List<SemanticToken>();
            }

            var originalLines = targetFile.Content.Split('\n');
            var cleanLineToOriginal = new List<int>();
            for (int i = 0; i < originalLines.Length; i++)
            {
                var trimmed = originalLines[i].Trim();
                if (trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
                {
                    continue;
                }
                cleanLineToOriginal.Add(i);
            }

            if (cleanLineToOriginal.Count == 0)
            {
                return new List<SemanticToken>();
            }

            var header = $"// File: {targetFile.FileName}";
            var combinedLines = combinedCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var headerLineIndex = -1;
            for (int i = 0; i < combinedLines.Length; i++)
            {
                if (string.Equals(combinedLines[i].TrimEnd('\r'), header, StringComparison.Ordinal))
                {
                    headerLineIndex = i;
                    break;
                }
            }

            if (headerLineIndex < 0)
            {
                return new List<SemanticToken>();
            }

            var startLine = headerLineIndex + 1;
            var endLine = startLine + cleanLineToOriginal.Count;
            var mappedTokens = new List<SemanticToken>(decodedTokens.Count);

            foreach (var token in decodedTokens)
            {
                if (token.Line < startLine || token.Line >= endLine)
                {
                    continue;
                }

                var cleanIndex = token.Line - startLine;
                if (cleanIndex < 0 || cleanIndex >= cleanLineToOriginal.Count)
                {
                    continue;
                }

                var originalLine = cleanLineToOriginal[cleanIndex];

                mappedTokens.Add(new SemanticToken
                {
                    Line = originalLine,
                    Character = token.Character,
                    Length = token.Length,
                    TokenType = token.TokenType,
                    TokenModifiers = token.TokenModifiers
                });
            }

            mappedTokens.Sort((a, b) =>
            {
                var lineCompare = a.Line.CompareTo(b.Line);
                return lineCompare != 0 ? lineCompare : a.Character.CompareTo(b.Character);
            });

            return mappedTokens;
        }

        private static List<SemanticToken> DecodeSemanticTokens(List<int> data)
        {
            var tokens = new List<SemanticToken>();
            int currentLine = 0;
            int currentChar = 0;

            for (int i = 0; i + 4 < data.Count; i += 5)
            {
                var deltaLine = data[i];
                var deltaStart = data[i + 1];
                var length = data[i + 2];
                var tokenType = data[i + 3];
                var modifiers = data[i + 4];

                currentLine += deltaLine;
                currentChar = deltaLine == 0 ? currentChar + deltaStart : deltaStart;

                tokens.Add(new SemanticToken
                {
                    Line = currentLine,
                    Character = currentChar,
                    Length = length,
                    TokenType = tokenType,
                    TokenModifiers = modifiers
                });
            }

            return tokens;
        }

        private static List<int> EncodeSemanticTokens(List<SemanticToken> tokens)
        {
            var data = new List<int>(tokens.Count * 5);
            int previousLine = 0;
            int previousCharacter = 0;

            foreach (var token in tokens)
            {
                var deltaLine = token.Line - previousLine;
                var deltaStart = deltaLine == 0 ? token.Character - previousCharacter : token.Character;

                data.Add(deltaLine);
                data.Add(deltaStart);
                data.Add(token.Length);
                data.Add(token.TokenType);
                data.Add(token.TokenModifiers);

                previousLine = token.Line;
                previousCharacter = token.Character;
            }

            return data;
        }

        private static string IndentCode(string code)
        {
            var lines = code.Split('\n');
            return string.Join("\n", lines.Select(line => string.IsNullOrWhiteSpace(line) ? line : "    " + line));
        }

        private static int GetAdjustedPosition(List<FileContent> files, string targetFileName, int originalPosition)
        {
            // Calculate the position adjustment for the target file in the combined code
            var position = 0;

            // Add using statements count
            var allUsingStatements = new HashSet<string>();
            foreach (var file in files)
            {
                var lines = file.Content.Split('\n');
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("using ") && trimmedLine.EndsWith(";"))
                    {
                        allUsingStatements.Add(trimmedLine);
                    }
                }
            }

            // Each AppendLine adds an Environment.NewLine; the cleanContent preserves "\n" between lines.
            position += allUsingStatements.Sum(u => u.Length + Environment.NewLine.Length) + Environment.NewLine.Length; // extra blank line

            // Add content before target file
            foreach (var file in files)
            {
                if (file.FileName == targetFileName)
                {
                    break;
                }

                position += $"// File: {file.FileName}".Length + Environment.NewLine.Length;
                var cleanContent = RemoveUsingStatements(file.Content);
                // cleanContent itself uses "\n" newlines; AppendLine adds one Environment.NewLine after it
                position += cleanContent.Length + Environment.NewLine.Length; // content + trailing EOL from AppendLine

                position += Environment.NewLine.Length; // Extra blank line after each file
            }

            // Add the target file header
            position += $"// File: {targetFileName}".Length + Environment.NewLine.Length;

            // Convert original position to the cleaned content
            var targetFile = files.First(f => f.FileName == targetFileName);
            var targetCleanContent = RemoveUsingStatements(targetFile.Content);
            var originalLines = targetFile.Content.Split('\n');
            var cleanLines = targetCleanContent.Split('\n');

            // Find the character position in the clean content
            var currentPos = 0;
            var linesProcessed = 0;
            var originalChar = 0;

            foreach (var line in originalLines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("using ") && trimmedLine.EndsWith(";"))
                {
                    // Skip using statements
                    originalChar += line.Length + 1;
                    continue;
                }

                if (originalChar + line.Length >= originalPosition)
                {
                    // We found the target line
                    var positionInLine = originalPosition - originalChar;
                    return position + currentPos + positionInLine;
                }

                currentPos += line.Length + 1; // clean content uses \n
                originalChar += line.Length + 1; // original uses \n
                linesProcessed++;
            }

            return position + originalPosition;
        }

        private static CodeCheckResult[] AdjustCodeCheckResults(CodeCheckResult[] results, List<FileContent> files, string targetFileName, string combinedCode)
        {
            if (files == null || files.Count == 0 || string.IsNullOrEmpty(targetFileName))
            {
                return results ?? Array.Empty<CodeCheckResult>();
            }

            var targetFile = files.FirstOrDefault(f => f.FileName == targetFileName) ?? files.First();
            var targetCleanContent = RemoveUsingStatements(targetFile.Content);

            // Compute the start of the target file's clean content within the combined code
            var header = $"// File: {targetFile.FileName}" + Environment.NewLine;
            var headerIndex = combinedCode.IndexOf(header, StringComparison.Ordinal);
            if (headerIndex < 0)
            {
                return results ?? Array.Empty<CodeCheckResult>();
            }
            var targetStartInCombined = headerIndex + header.Length;
            var targetEndInCombined = targetStartInCombined + targetCleanContent.Length;

            // Build line start maps for original and clean content to convert offsets back
            static List<int> BuildLineStarts(string text)
            {
                var starts = new List<int> { 0 };
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        starts.Add(i + 1);
                    }
                }
                return starts;
            }

            var originalLines = targetFile.Content.Split('\n');
            var isUsingLine = originalLines.Select(l => l.Trim().StartsWith("using ") && l.Trim().EndsWith(";")).ToArray();

            var nonUsingOriginalIndices = new List<int>();
            for (int i = 0; i < originalLines.Length; i++)
            {
                if (!isUsingLine[i]) nonUsingOriginalIndices.Add(i);
            }

            var cleanContent = targetCleanContent;
            var cleanLineStarts = BuildLineStarts(cleanContent);

            // Precompute original line starts
            var originalContent = targetFile.Content;
            var originalLineStarts = BuildLineStarts(originalContent);

            int CleanOffsetToOriginal(int cleanOffset)
            {
                // Identify the clean line index
                int lineIdx = 0;
                while (lineIdx + 1 < cleanLineStarts.Count && cleanLineStarts[lineIdx + 1] <= cleanOffset)
                {
                    lineIdx++;
                }

                int colInLine = cleanOffset - cleanLineStarts[lineIdx];
                if (lineIdx >= nonUsingOriginalIndices.Count)
                {
                    // Out of bounds; clamp to end
                    return originalContent.Length;
                }
                int origLineIdx = nonUsingOriginalIndices[lineIdx];
                int origLineStart = originalLineStarts[origLineIdx];
                return Math.Min(origLineStart + colInLine, originalContent.Length);
            }

            var mapped = new List<CodeCheckResult>(results?.Length ?? 0);
            foreach (var r in results ?? Array.Empty<CodeCheckResult>())
            {
                // Keep only diagnostics that fall within the target file's content region
                if (r.OffsetTo <= targetStartInCombined || r.OffsetFrom >= targetEndInCombined)
                {
                    continue;
                }

                var fromClean = Math.Max(0, r.OffsetFrom - targetStartInCombined);
                var toClean = Math.Max(0, Math.Min(r.OffsetTo, targetEndInCombined) - targetStartInCombined);

                var newFrom = CleanOffsetToOriginal(fromClean);
                var newTo = CleanOffsetToOriginal(toClean);

                mapped.Add(new CodeCheckResult
                {
                    Id = r.Id,
                    Keyword = r.Keyword,
                    Message = r.Message,
                    OffsetFrom = newFrom,
                    OffsetTo = Math.Max(newFrom, newTo),
                    Severity = r.Severity,
                    SeverityNumeric = r.SeverityNumeric
                });
            }

            return mapped.ToArray();
        }

        private class CSharpLanguage : ILanguageService
        {
            private static readonly LanguageVersion MaxLanguageVersion = Enum
                .GetValues(typeof(LanguageVersion))
                .Cast<LanguageVersion>()
                .Max();

            public SyntaxTree ParseText(string sourceCode, SourceCodeKind kind)
            {
                var options = new CSharpParseOptions(kind: kind, languageVersion: MaxLanguageVersion);

                // Return a syntax tree of our source code
                return CSharpSyntaxTree.ParseText(sourceCode, options);
            }

            public Compilation CreateLibraryCompilation(string assemblyName, bool enableOptimisations)
            {
                throw new NotImplementedException();
            }
        }
    }
}

