using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeActionResult : IResponse
    {
        public string Title { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public bool IsPreferred { get; set; }
        public string ActionId { get; set; } = string.Empty;
        public List<CodeEdit> Edits { get; set; } = new();
        public object Data { get; set; }
    }

    public class CodeEdit
    {
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public string NewText { get; set; } = string.Empty;
    }
}