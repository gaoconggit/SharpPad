using MonacoRoslynCompletionProvider;
using MonacoRoslynCompletionProvider.Api;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/completion/{0}", async (e) =>
{
    using var reader = new StreamReader(e.Request.Body);
    string text = await reader.ReadToEndAsync();
    if (text != null)
    {
        if (e.Request.Path.Value?.EndsWith("complete") == true)
        {
            var tabCompletionRequest = JsonSerializer.Deserialize<TabCompletionRequest>(text);
            var tabCompletionResults = await CompletitionRequestHandler.Handle(tabCompletionRequest);
            await JsonSerializer.SerializeAsync(e.Response.Body, tabCompletionResults);
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("signature") == true)
        {
            var signatureHelpRequest = JsonSerializer.Deserialize<SignatureHelpRequest>(text);
            var signatureHelpResult = await CompletitionRequestHandler.Handle(signatureHelpRequest);
            await JsonSerializer.SerializeAsync(e.Response.Body, signatureHelpResult);
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("hover") == true)
        {
            var hoverInfoRequest = JsonSerializer.Deserialize<HoverInfoRequest>(text);
            var hoverInfoResult = await CompletitionRequestHandler.Handle(hoverInfoRequest);
            await JsonSerializer.SerializeAsync(e.Response.Body, hoverInfoResult);
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("codeCheck") == true)
        {
            var codeCheckRequest = JsonSerializer.Deserialize<CodeCheckRequest>(text);
            var codeCheckResults = await CompletitionRequestHandler.Handle(codeCheckRequest);
            await JsonSerializer.SerializeAsync(e.Response.Body, codeCheckResults);
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("format") == true)
        {
            var codeFormatRequest = JsonSerializer.Deserialize<CodeFormatRequest>(text);
            var codeCheckResults = CompletitionRequestHandler.FormatCode(codeFormatRequest?.SourceCode);
            await JsonSerializer.SerializeAsync(e.Response.Body, new
            {
                code = 0,
                data = codeCheckResults
            });
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("run") == true)
        {
            var codeRunRequest = JsonSerializer.Deserialize<CodeRunRequest>(text);
            string nugetPackages = string.Join(" ", codeRunRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var codeCheckResults = CodeRunner.RunProgramCode(codeRunRequest?.SourceCode, nugetPackages);
            await JsonSerializer.SerializeAsync(e.Response.Body, new
            {
                code = 0,
                data = codeCheckResults
            });
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("addPackages") == true)
        {
            try
            {
                var addPackagesRequest = JsonSerializer.Deserialize<AddPackagesRequest>(text);
                string nugetPackages = string.Join(" ", addPackagesRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
                CodeRunner.DownloadPackage(nugetPackages);
                await JsonSerializer.SerializeAsync(e.Response.Body, new
                {
                    code = 0,
                    data = default(object)
                });
            }
            catch (Exception ex)
            {
                await JsonSerializer.SerializeAsync(e.Response.Body, new
                {
                    code = 500,
                    data = default(object),
                    message = ex.ToString()
                });
            }
            return;
        }
    }

    e.Response.StatusCode = 405;
});

app.UseFileServer();

app.Run();
