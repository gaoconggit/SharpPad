using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeRunRequest
    {
        public string SourceCode { get; set; }

        public List<Package> Packages { get; set; }
    }

    public record Package(string Id, string Version);
}
