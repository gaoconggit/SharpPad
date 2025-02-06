using MonacoRoslynCompletionProvider;
using MonacoRoslynCompletionProvider.Api;
using System.Text.Json;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .AddNewtonsoftJson(); // Use Newtonsoft.Json

// 添加Swagger服务
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
            string nugetPackages = string.Join(" ", tabCompletionRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var tabCompletionResults = await CompletitionRequestHandler.Handle(tabCompletionRequest, nugetPackages);
            await JsonSerializer.SerializeAsync(e.Response.Body, tabCompletionResults);
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("signature") == true)
        {
            var signatureHelpRequest = JsonSerializer.Deserialize<SignatureHelpRequest>(text);
            string nugetPackages = string.Join(" ", signatureHelpRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var signatureHelpResult = await CompletitionRequestHandler.Handle(signatureHelpRequest, nugetPackages);
            await JsonSerializer.SerializeAsync(e.Response.Body, signatureHelpResult);
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("hover") == true)
        {
            var hoverInfoRequest = JsonSerializer.Deserialize<HoverInfoRequest>(text);
            string nugetPackages = string.Join(" ", hoverInfoRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var hoverInfoResult = await CompletitionRequestHandler.Handle(hoverInfoRequest, nugetPackages);
            await JsonSerializer.SerializeAsync(e.Response.Body, hoverInfoResult);
            return;
        }
        else if (e.Request.Path.Value?.EndsWith("codeCheck") == true)
        {
            var codeCheckRequest = JsonSerializer.Deserialize<CodeCheckRequest>(text);
            string nugetPackages = string.Join(" ", codeCheckRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var codeCheckResults = await CompletitionRequestHandler.Handle(codeCheckRequest, nugetPackages);
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

            // 设置响应头，允许流式输出
            e.Response.Headers.TryAdd("Content-Type", "text/event-stream");
            e.Response.Headers.TryAdd("Cache-Control", "no-cache");
            e.Response.Headers.TryAdd("Connection", "keep-alive");

            // 创建输出和错误回调
            async void OnOutput(string output)
            {
                await e.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "output", content = output })}\n\n");
                await e.Response.Body.FlushAsync();
            }

            async void OnError(string error)
            {
                await e.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", content = error })}\n\n");
                await e.Response.Body.FlushAsync();
            }

            var result = await CodeRunner.RunProgramCodeAsync(
                codeRunRequest?.SourceCode,
                nugetPackages,
                OnOutput,
                OnError
            );

            await e.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "completed", result })}\n\n");
            await e.Response.Body.FlushAsync();
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
// 使用Swagger中间件
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
});

app.MapControllers();
app.Run();
