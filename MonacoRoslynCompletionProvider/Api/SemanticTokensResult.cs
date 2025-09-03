using System.Collections.Generic;

namespace MonacoRoslynCompletionProvider.Api
{
    public class SemanticTokensResult : IResponse
    {
        public List<int> Data { get; set; } = new();
    }

    public class SemanticToken
    {
        public int Line { get; set; }
        public int Character { get; set; }
        public int Length { get; set; }
        public int TokenType { get; set; }
        public int TokenModifiers { get; set; }
    }

    /// <summary>
    /// 语义令牌类型 - 对应 Monaco Editor 的 TokenType
    /// </summary>
    public enum SemanticTokenType
    {
        Namespace = 0,
        Class = 1,
        Enum = 2,
        Interface = 3,
        Struct = 4,
        TypeParameter = 5,
        Parameter = 6,
        Variable = 7,
        Property = 8,
        EnumMember = 9,
        Event = 10,
        Function = 11,
        Method = 12,
        Macro = 13,
        Keyword = 14,
        Modifier = 15,
        Comment = 16,
        String = 17,
        Number = 18,
        Regexp = 19,
        Operator = 20
    }

    /// <summary>
    /// 语义令牌修饰符 - 对应 Monaco Editor 的 TokenModifier
    /// </summary>
    [System.Flags]
    public enum SemanticTokenModifier
    {
        Declaration = 1,
        Definition = 2,
        Readonly = 4,
        Static = 8,
        Deprecated = 16,
        Abstract = 32,
        Async = 64,
        Modification = 128,
        Documentation = 256,
        DefaultLibrary = 512
    }
}