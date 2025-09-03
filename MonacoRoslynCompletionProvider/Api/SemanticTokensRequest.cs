using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class SemanticTokensRequest : IRequest
    {
        public string Code { get; set; } = string.Empty;
        public List<NugetPackage> Packages { get; set; } = new();
    }
}