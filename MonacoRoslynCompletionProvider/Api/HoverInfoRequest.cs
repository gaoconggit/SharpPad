using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class HoverInfoRequest : IRequest
    {
        public HoverInfoRequest()
        { }


        public virtual string Code { get; set; }

        public virtual int Position { get; set; }

        public List<Package> Packages { get; set; }

        public string ProjectType { get; set; }
    }
}
