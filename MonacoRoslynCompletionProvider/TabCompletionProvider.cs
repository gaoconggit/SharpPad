using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using MonacoRoslynCompletionProvider.Api;
using System;
using System.Threading.Tasks;

namespace MonacoRoslynCompletionProvider
{
    internal class TabCompletionProvider
    {
        // Thanks to https://www.strathweb.com/2018/12/using-roslyn-c-completion-service-programmatically/
        public async Task<TabCompletionResult[]> Provide(Document document, int position)
        {
            var completionService = CompletionService.GetService(document);
            if (completionService == null)
            {
                return Array.Empty<TabCompletionResult>();
            }

            var results = await completionService.GetCompletionsAsync(document, position);
            if (results == null || results.Items == null || results.Items.Length == 0)
            {
                return Array.Empty<TabCompletionResult>();
            }

            var tabCompletionDTOs = new TabCompletionResult[results.Items.Length];
            var suggestions = new string[results.Items.Length];

            for (int i = 0; i < results.Items.Length; i++)
            {
                var itemDescription = await completionService.GetDescriptionAsync(document, results.Items[i]);

                var dto = new TabCompletionResult();
                dto.Suggestion = results.Items[i].DisplayText;
                dto.Description = itemDescription.Text;

                tabCompletionDTOs[i] = dto;
                suggestions[i] = results.Items[i].DisplayText;
            }

            return tabCompletionDTOs;
        }
    }
}
