using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeActionRequest : IRequest
    {
        public string Code { get; set; } = string.Empty;
        public int Position { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public List<NugetPackage> Packages { get; set; } = new();
        public string ProjectType { get; set; }
    }
}