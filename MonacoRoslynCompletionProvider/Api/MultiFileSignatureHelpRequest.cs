using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class MultiFileSignatureHelpRequest : IRequest
    {
        public MultiFileSignatureHelpRequest()
        {
            Files = new List<FileContent>();
            Packages = new List<Package>();
        }

        /// <summary>
        /// List of files for signature help context
        /// </summary>
        public List<FileContent> Files { get; set; }

        /// <summary>
        /// The specific file ID where signature help is requested
        /// </summary>
        public string TargetFileId { get; set; }

        /// <summary>
        /// Position in the target file for signature help
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// NuGet packages to include
        /// </summary>
        public List<Package> Packages { get; set; }

        /// <summary>
        /// For backward compatibility with single file requests
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// Whether this is a multi-file request
        /// </summary>
        public bool IsMultiFile => Files != null && Files.Count > 0;
    }
}