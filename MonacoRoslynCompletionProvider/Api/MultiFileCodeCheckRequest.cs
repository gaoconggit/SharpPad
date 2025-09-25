using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class MultiFileCodeCheckRequest : IRequest
    {
        public MultiFileCodeCheckRequest()
        {
            Files = new List<FileContent>();
            Packages = new List<Package>();
        }

        /// <summary>
        /// List of files to check
        /// </summary>
        public List<FileContent> Files { get; set; }

        /// <summary>
        /// NuGet packages to include
        /// </summary>
        public List<Package> Packages { get; set; }

        /// <summary>
        /// The specific file ID/name whose diagnostics should be mapped
        /// back to original offsets for the active editor file.
        /// </summary>
        public string TargetFileId { get; set; }

        /// <summary>
        /// For backward compatibility with single file requests
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// Whether this is a multi-file request
        /// </summary>
        public bool IsMultiFile => Files != null && Files.Count > 0;

        /// <summary>
        /// Project type (e.g., "console", "winforms", "web")
        /// </summary>
        public string ProjectType { get; set; }
    }
}
