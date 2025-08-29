using Microsoft.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynPad.Roslyn.QuickInfo;

public interface IQuickInfoProvider
{
    Task<QuickInfoItem?> GetItemAsync(Document document, int position, CancellationToken cancellationToken = default);
}
