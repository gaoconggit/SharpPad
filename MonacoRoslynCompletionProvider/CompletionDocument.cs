using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using MonacoRoslynCompletionProvider.Api;
//using MonacoRoslynCompletionProvider.RoslynPad;
//using RoslynPad.Roslyn.QuickInfo;
using System.Threading;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider
{
    public class CompletionDocument
    {
        public Document Document { get; }
        public SemanticModel SemanticModel { get; }
        public EmitResult EmitResult { get; }

        //public CompletionDocument(Document document, SemanticModel semanticModel, EmitResult emitResult)
        //{
        //    Document = document;
        //    SemanticModel = semanticModel;
        //    EmitResult = emitResult;
        //}

        //private QuickInfoProvider quickInfoProvider;

        internal CompletionDocument(Document document, SemanticModel semanticModel, EmitResult emitResult)
        {
            this.Document = document;
            this.SemanticModel = semanticModel;
            this.EmitResult = emitResult;

            //this.quickInfoProvider = new QuickInfoProvider(new DeferredQuickInfoContentProvider());
        }

        public Task<HoverInfoResult> GetHoverInformation(int position, CancellationToken cancellationToken)
        {
            //var info = await quickInfoProvider.GetItemAsync(document, position, cancellationToken);
            //return new HoverInfoResult() { Information = info.Create().ToString() };
            var hoverInformationProvider = new HoverInformationProvider();
            return hoverInformationProvider.Provide(Document, position, SemanticModel);
        }

        public Task<TabCompletionResult[]> GetTabCompletion(int position, CancellationToken cancellationToken)
        {
            var tabCompletionProvider = new TabCompletionProvider();
            return tabCompletionProvider.Provide(Document, position);
        }

        public async Task<CodeCheckResult[]> GetCodeCheckResults(CancellationToken cancellationToken)
        {
            var codeCheckProvider = new CodeCheckProvider();
            return await codeCheckProvider.Provide(EmitResult, Document, cancellationToken);
        }

        public Task<SignatureHelpResult> GetSignatureHelp(int position, CancellationToken cancellationToken)
        {
            var signatureHelpProvider = new SignatureHelpProvider();
            return signatureHelpProvider.Provide(Document, position, SemanticModel);
        }

    }
}
