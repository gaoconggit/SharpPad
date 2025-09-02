using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class DefinitionRequest : IRequest
    {
        public string Code { get; set; } = string.Empty;
        public int Position { get; set; }
        public List<NugetPackage> Packages { get; set; } = new();
    }
    
    public class NugetPackage
    {
        public string Id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}