using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class RemovePackagesRequest
    {
        public List<Package> Packages { get; set; }
    }
}
