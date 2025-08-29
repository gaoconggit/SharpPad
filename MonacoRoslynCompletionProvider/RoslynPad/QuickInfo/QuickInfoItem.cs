using Microsoft.CodeAnalysis.Text;
using System;

namespace RoslynPad.Roslyn.QuickInfo;

public sealed class QuickInfoItem
{
    private readonly Func<object> _contentFactory;

    public TextSpan TextSpan { get; }

    public object Create() => _contentFactory();

    internal QuickInfoItem(TextSpan textSpan, Func<object> contentFactory)
    {
        TextSpan = textSpan;
        _contentFactory = contentFactory;
    }
}
