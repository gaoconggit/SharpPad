using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonacoRoslynCompletionProvider.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Text;

namespace MonacoRoslynCompletionProvider
{
    internal class SignatureHelpProvider
    {
        public async Task<SignatureHelpResult> Provide(Document document, int position, SemanticModel semanticModel)
        {
            var invocation = await InvocationContext.GetInvocation(document, position);
            if (invocation == null) return null;

            int activeParameter = 0;
            foreach (var comma in invocation.Separators)
            {
                if (comma.Span.Start > invocation.Position)
                    break;

                activeParameter += 1;
            }

            var signaturesSet = new HashSet<Signatures>();
            var bestScore = int.MinValue;
            Signatures bestScoredItem = null;

            var types = invocation.ArgumentTypes;
            ISymbol throughSymbol = null;
            ISymbol throughType = null;
            var methodGroup = invocation.SemanticModel.GetMemberGroup(invocation.Receiver).OfType<IMethodSymbol>();
            if (invocation.Receiver is MemberAccessExpressionSyntax)
            {
                var throughExpression = ((MemberAccessExpressionSyntax)invocation.Receiver).Expression;
                var typeInfo = semanticModel.GetTypeInfo(throughExpression);
                throughSymbol = invocation.SemanticModel.GetSpeculativeSymbolInfo(invocation.Position, throughExpression, SpeculativeBindingOption.BindAsExpression).Symbol;
                throughType = invocation.SemanticModel.GetSpeculativeTypeInfo(invocation.Position, throughExpression, SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
                var includeInstance = (throughSymbol != null && !(throughSymbol is ITypeSymbol)) ||
                    throughExpression is LiteralExpressionSyntax ||
                    throughExpression is TypeOfExpressionSyntax;
                var includeStatic = (throughSymbol is INamedTypeSymbol) || throughType != null;
                if (throughType == null)
                {
                    throughType = typeInfo.Type;
                    includeInstance = true;
                }
                methodGroup = methodGroup.Where(m => (m.IsStatic && includeStatic) || (!m.IsStatic && includeInstance));
            }
            else if (invocation.Receiver is SimpleNameSyntax && invocation.IsInStaticContext)
            {
                methodGroup = methodGroup.Where(m => m.IsStatic || m.MethodKind == MethodKind.LocalFunction);
            }

            foreach (var methodOverload in methodGroup)
            {
                var signature = BuildSignature(methodOverload);
                signaturesSet.Add(signature);

                var score = InvocationScore(methodOverload, types);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestScoredItem = signature;
                }
            }

            return new SignatureHelpResult()
            {
                Signatures = signaturesSet.ToArray(),
                ActiveParameter = activeParameter,
                ActiveSignature = Array.IndexOf(signaturesSet.ToArray(), bestScoredItem)
            };
        }

        private static Signatures BuildSignature(IMethodSymbol symbol)
        {
            var parameters = new List<Parameter>();
            var xmlDoc = symbol.GetDocumentationCommentXml();
            var parameterDocs = ParseParameterDocumentation(xmlDoc);
            
            foreach (var parameter in symbol.Parameters)
            {
                var paramLabel = parameter.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                var paramDoc = parameterDocs.TryGetValue(parameter.Name, out var doc) ? doc : "";
                parameters.Add(new Parameter() { 
                    Label = paramLabel,
                    Documentation = paramDoc
                });
            };
            
            var signature = new Signatures
            {
                Documentation = ParseMethodDocumentation(xmlDoc),
                Label = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Parameters = parameters.ToArray()
            };

            return signature;
        }
        
        private static string ParseMethodDocumentation(string xmlDoc)
        {
            if (string.IsNullOrEmpty(xmlDoc))
                return "";
                
            try
            {
                var doc = XDocument.Parse(xmlDoc);
                var summary = doc.Descendants("summary").FirstOrDefault()?.Value?.Trim();
                var returns = doc.Descendants("returns").FirstOrDefault()?.Value?.Trim();
                
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(summary))
                {
                    sb.AppendLine(summary);
                }
                if (!string.IsNullOrEmpty(returns))
                {
                    sb.AppendLine($"Returns: {returns}");
                }
                
                return sb.ToString().Trim();
            }
            catch
            {
                return xmlDoc;
            }
        }
        
        private static Dictionary<string, string> ParseParameterDocumentation(string xmlDoc)
        {
            var paramDocs = new Dictionary<string, string>();
            
            if (string.IsNullOrEmpty(xmlDoc))
                return paramDocs;
                
            try
            {
                var doc = XDocument.Parse(xmlDoc);
                var paramElements = doc.Descendants("param");
                
                foreach (var param in paramElements)
                {
                    var name = param.Attribute("name")?.Value;
                    var description = param.Value?.Trim();
                    
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(description))
                    {
                        paramDocs[name] = description;
                    }
                }
            }
            catch
            {
                // 解析失败时返回空字典
            }
            
            return paramDocs;
        }

        private int InvocationScore(IMethodSymbol symbol, IEnumerable<TypeInfo> types)
        {
            var parameters = symbol.Parameters;
            if (parameters.Count() < types.Count())
                return int.MinValue;

            var score = 0;
            var invocationEnum = types.GetEnumerator();
            var definitionEnum = parameters.GetEnumerator();
            while (invocationEnum.MoveNext() && definitionEnum.MoveNext())
            {
                if (invocationEnum.Current.ConvertedType == null)
                    score += 1;

                else if (SymbolEqualityComparer.Default.Equals(invocationEnum.Current.ConvertedType, definitionEnum.Current.Type))
                    score += 2;
            }
            return score;
        }
    }
}
