using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class TabCompletionRequest : IRequest
    {
        public TabCompletionRequest()
        { }

        public virtual string Code { get; set; }

        public virtual int Position { get; set; }


        public List<Package> Packages { get; set; }
    }
}
