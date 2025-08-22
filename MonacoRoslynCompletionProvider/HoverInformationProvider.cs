using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonacoRoslynCompletionProvider.Api;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider
{
    internal class HoverInformationProvider
    {
        public async Task<HoverInfoResult> Provide(Document document, int position, SemanticModel semanticModel)
        {
            TypeInfo typeInfo;
            var syntaxRoot = await document.GetSyntaxRootAsync();

            if (syntaxRoot == null)
            {
                return null;
            }

            var expressionNode = syntaxRoot.FindToken(position).Parent;
            Location location;
            switch (expressionNode)
            {
                case VariableDeclaratorSyntax:
                    {
                        SyntaxNode childNode = expressionNode.ChildNodes()?.FirstOrDefault()?.ChildNodes()?.FirstOrDefault();
                        typeInfo = semanticModel.GetTypeInfo(childNode!);
                        location = expressionNode.GetLocation();
                        if (typeInfo.Type != null)
                        {
                            return new HoverInfoResult()
                            {
                                Information = typeInfo.Type.ToString(),
                                OffsetFrom = location.SourceSpan.Start,
                                OffsetTo = location.SourceSpan.End
                            };
                        }

                        break;
                    }
                case PropertyDeclarationSyntax prop:
                    {
                        location = expressionNode.GetLocation();
                        return new HoverInfoResult() { Information = prop.Type.ToString(), OffsetFrom = location.SourceSpan.Start, OffsetTo = location.SourceSpan.End };
                    }
                case ParameterSyntax p:
                    {
                        location = expressionNode.GetLocation();
                        if (p.Type != null)
                        {
                            return new HoverInfoResult()
                            {
                                Information = p.Type.ToString(),
                                OffsetFrom = location.SourceSpan.Start,
                                OffsetTo = location.SourceSpan.End
                            };
                        }

                        break;
                    }
                case IdentifierNameSyntax i:
                    {
                        location = expressionNode.GetLocation();
                        typeInfo = semanticModel.GetTypeInfo(i);
                        if (typeInfo.Type != null)
                            return new HoverInfoResult() { Information = typeInfo.Type.ToString(), OffsetFrom = location.SourceSpan.Start, OffsetTo = location.SourceSpan.End };
                        break;
                    }
            }

            var symbolInfo = semanticModel.GetSymbolInfo(expressionNode!);
            if (symbolInfo.Symbol == null)
            {
                return null;
            }


            location = expressionNode.GetLocation();
            return new HoverInfoResult()
            {
                Information = HoverInfoBuilder.Build(symbolInfo),
                OffsetFrom = location.SourceSpan.Start,
                OffsetTo = location.SourceSpan.End
            };

        }
    }
}
