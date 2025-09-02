using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class DefinitionResult : IResponse
    {
        public List<DefinitionLocation> Locations { get; set; } = new();
    }

    public class DefinitionLocation
    {
        public string? Uri { get; set; }
        public Range Range { get; set; } = new();
    }

    public class Range
    {
        public Position StartPosition { get; set; } = new();
        public Position EndPosition { get; set; } = new();
    }

    public class Position
    {
        public int LineNumber { get; set; }
        public int Column { get; set; }
    }
}