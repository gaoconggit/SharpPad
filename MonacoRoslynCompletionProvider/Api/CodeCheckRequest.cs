using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class CodeCheckRequest : IRequest
    {
        public CodeCheckRequest()
        { }

        public virtual string Code { get; set; }

        public List<Package> Packages { get; set; }

        public string ProjectType { get; set; }
    }
}
