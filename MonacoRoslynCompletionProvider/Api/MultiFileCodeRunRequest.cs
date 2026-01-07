using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider.Api
{
    public class MultiFileCodeRunRequest
    {
        public List<FileContent> Files { get; set; }
        public List<Package> Packages { get; set; }
        public int LanguageVersion { get; set; }
        public string SessionId { get; set; }
        public string ProjectType { get; set; } = "console";
        public List<int> BreakpointLines { get; set; }
        public string BreakpointFileName { get; set; }

        // For backward compatibility
        public string SourceCode { get; set; }

        public bool IsMultiFile => Files != null && Files.Count > 0;
    }

    public class FileContent
    {
        public string FileName { get; set; }
        public string Content { get; set; }
        public bool IsEntry { get; set; } // Marks if this file contains the Main method
    }
}
