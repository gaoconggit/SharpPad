using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider.Api
{
    public class ExeBuildRequest
    {
        public List<FileContent> Files { get; set; }
        public List<Package> Packages { get; set; }
        public int LanguageVersion { get; set; }
        public string OutputFileName { get; set; } = "Program.exe";

        // For backward compatibility with single file projects
        public string SourceCode { get; set; }

        public bool IsMultiFile => Files != null && Files.Count > 0;
    }

    public class ExeBuildResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string ExeFilePath { get; set; }
        public long FileSizeBytes { get; set; }
        public List<string> CompilationMessages { get; set; } = new List<string>();
    }
}