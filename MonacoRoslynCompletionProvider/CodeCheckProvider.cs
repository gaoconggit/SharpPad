using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using MonacoRoslynCompletionProvider.Api;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider
{
    public class CodeCheckProvider
    {
        public CodeCheckProvider()
        {
        }

        public async Task<CodeCheckResult[]> Provide(EmitResult emitResult, Document document, CancellationToken cancellationToken)
        {
            var result = new List<CodeCheckResult>();
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            foreach (var r in emitResult.Diagnostics)
            {
                var sev = r.Severity == DiagnosticSeverity.Error ? CodeCheckSeverity.Error : r.Severity == DiagnosticSeverity.Warning ? CodeCheckSeverity.Warning : r.Severity == DiagnosticSeverity.Info ? CodeCheckSeverity.Info : CodeCheckSeverity.Hint;
                var keyword = string.Empty;
                var offsetFrom = 0;
                var offsetTo = 0;
                if (r.Location != null && r.Location.IsInSource)
                {
                    var span = r.Location.SourceSpan;
                    if (span.Start >= 0 && span.End <= sourceText.Length && span.Length >= 0)
                    {
                        keyword = sourceText.GetSubText(span).ToString();
                        offsetFrom = span.Start;
                        offsetTo = span.End;
                    }
                }
                var msg = new CodeCheckResult() { Id = r.Id, Keyword = keyword, Message = r.GetMessage(), OffsetFrom = offsetFrom, OffsetTo = offsetTo, Severity = sev, SeverityNumeric = (int)sev };
                result.Add(msg);
            }
            return result.Where(m => m.Severity == CodeCheckSeverity.Error).ToArray();
        }
    }
}
