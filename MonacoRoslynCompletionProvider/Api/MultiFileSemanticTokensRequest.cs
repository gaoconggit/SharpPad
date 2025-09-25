using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class MultiFileSemanticTokensRequest : IRequest
    {
        public MultiFileSemanticTokensRequest()
        {
            Files = new List<FileContent>();
            Packages = new List<Package>();
        }

        /// <summary>
        /// Source files participating in the semantic analysis context.
        /// </summary>
        public List<FileContent> Files { get; set; }

        /// <summary>
        /// NuGet packages referenced by the workspace.
        /// </summary>
        public List<Package> Packages { get; set; }

        /// <summary>
        /// Identifier of the active file whose tokens should be mapped back.
        /// </summary>
        public string TargetFileId { get; set; }

        /// <summary>
        /// Backward compatibility payload for single-file callers.
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// Indicates if the request contains multiple files.
        /// </summary>
        public bool IsMultiFile => Files != null && Files.Count > 0;

        /// <summary>
        /// Project type (e.g., "console", "winforms", "web")
        /// </summary>
        public string ProjectType { get; set; }
    }
}
