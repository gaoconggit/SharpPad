using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp;
using MonacoRoslynCompletionProvider.Api;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider
{
    public class DefinitionProvider
    {
        public async Task<DefinitionResult> Provide(Document document, int position, CancellationToken cancellationToken = default)
        {
            var result = new DefinitionResult();

            try
            {
                // 获取语义模型和语法树
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                
                if (semanticModel == null || syntaxTree == null)
                {
                    return result;
                }

                // 获取指定位置的语法节点
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var token = root.FindToken(position);
                
                if (token.IsKind(SyntaxKind.None))
                {
                    return result;
                }

                // 确保我们获取的是标识符 token
                if (!token.IsKind(SyntaxKind.IdentifierToken))
                {
                    // 如果不是标识符，尝试查找附近的标识符
                    var nearbyTokens = new[] {
                        root.FindToken(position - 1),
                        root.FindToken(position + 1)
                    };
                    
                    foreach (var nearbyToken in nearbyTokens)
                    {
                        if (nearbyToken.IsKind(SyntaxKind.IdentifierToken))
                        {
                            token = nearbyToken;
                            break;
                        }
                    }
                }

                // 获取符号信息 - 尝试多种方式
                ISymbol? symbol = null;
                
                // 方式1: 直接从标识符 token 获取符号
                if (token.IsKind(SyntaxKind.IdentifierToken))
                {
                    var node = token.Parent;
                    
                    // 尝试不同类型的语法节点
                    while (node != null && symbol == null)
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
                        symbol = symbolInfo.Symbol;
                        
                        // 如果没有找到直接符号，尝试候选符号
                        if (symbol == null && symbolInfo.CandidateSymbols.Length > 0)
                        {
                            symbol = symbolInfo.CandidateSymbols[0];
                        }
                        
                        // 尝试获取声明的符号（用于定义位置）
                        if (symbol == null)
                        {
                            symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
                        }
                        
                        if (symbol != null) break;
                        node = node.Parent;
                    }
                }

                // 方式2: 如果还是没找到，使用 SymbolFinder 在位置查找
                if (symbol == null)
                {
                    symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, cancellationToken);
                }

                if (symbol == null)
                {
                    return result;
                }

                // 查找符号的源定义
                var definition = await SymbolFinder.FindSourceDefinitionAsync(
                    symbol, document.Project.Solution, cancellationToken);

                var targetSymbol = definition ?? symbol;

                // 获取定义位置
                foreach (var location in targetSymbol.Locations)
                {
                    if (location.IsInSource)
                    {
                        var locationSyntaxTree = location.SourceTree;
                        
                        if (locationSyntaxTree != null)
                        {
                            var textSpan = location.SourceSpan;
                            var lineSpan = locationSyntaxTree.GetLineSpan(textSpan);
                            
                            var definitionLocation = new DefinitionLocation
                            {
                                Uri = locationSyntaxTree.FilePath,
                                Range = new Range
                                {
                                    StartPosition = new Position
                                    {
                                        LineNumber = lineSpan.StartLinePosition.Line + 1, // Monaco 使用 1-based line numbers
                                        Column = lineSpan.StartLinePosition.Character + 1  // Monaco 使用 1-based column numbers
                                    },
                                    EndPosition = new Position
                                    {
                                        LineNumber = lineSpan.EndLinePosition.Line + 1,
                                        Column = lineSpan.EndLinePosition.Character + 1
                                    }
                                }
                            };

                            result.Locations.Add(definitionLocation);
                        }
                    }
                }

                // 如果没有找到定义，尝试原始符号的位置
                if (!result.Locations.Any())
                {
                    foreach (var location in symbol.Locations.Where(l => l.IsInSource))
                    {
                        var locationSyntaxTree = location.SourceTree;
                        if (locationSyntaxTree != null)
                        {
                            var textSpan = location.SourceSpan;
                            var lineSpan = locationSyntaxTree.GetLineSpan(textSpan);
                            
                            var definitionLocation = new DefinitionLocation
                            {
                                Uri = locationSyntaxTree.FilePath,
                                Range = new Range
                                {
                                    StartPosition = new Position
                                    {
                                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                                        Column = lineSpan.StartLinePosition.Character + 1
                                    },
                                    EndPosition = new Position
                                    {
                                        LineNumber = lineSpan.EndLinePosition.Line + 1,
                                        Column = lineSpan.EndLinePosition.Character + 1
                                    }
                                }
                            };

                            result.Locations.Add(definitionLocation);
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // 忽略异常，返回空结果
            }

            return result;
        }
    }
}