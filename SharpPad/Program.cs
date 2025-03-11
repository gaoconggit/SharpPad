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

app.MapPost("/completion/{0}", async (HttpContext context) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        string text = await reader.ReadToEndAsync();
        if (text != null)
        {
            if (context.Request.Path.Value?.EndsWith("complete") == true)
            {
                var tabCompletionRequest = JsonSerializer.Deserialize<TabCompletionRequest>(text);
                string nugetPackages = string.Join(" ", tabCompletionRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
                var tabCompletionResults = await CompletitionRequestHandler.Handle(tabCompletionRequest, nugetPackages);
                await JsonSerializer.SerializeAsync(context.Response.Body, tabCompletionResults);
                return;
            }
            else if (context.Request.Path.Value?.EndsWith("signature") == true)
            {
                var signatureHelpRequest = JsonSerializer.Deserialize<SignatureHelpRequest>(text);
                string nugetPackages = string.Join(" ", signatureHelpRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
                var signatureHelpResult = await CompletitionRequestHandler.Handle(signatureHelpRequest, nugetPackages);
                await JsonSerializer.SerializeAsync(context.Response.Body, signatureHelpResult);
                return;
            }
            else if (context.Request.Path.Value?.EndsWith("hover") == true)
            {
                var hoverInfoRequest = JsonSerializer.Deserialize<HoverInfoRequest>(text);
                string nugetPackages = string.Join(" ", hoverInfoRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
                var hoverInfoResult = await CompletitionRequestHandler.Handle(hoverInfoRequest, nugetPackages);
                await JsonSerializer.SerializeAsync(context.Response.Body, hoverInfoResult);
                return;
            }
            else if (context.Request.Path.Value?.EndsWith("codeCheck") == true)
            {
                var codeCheckRequest = JsonSerializer.Deserialize<CodeCheckRequest>(text);
                string nugetPackages = string.Join(" ", codeCheckRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
                var codeCheckResults = await CompletitionRequestHandler.Handle(codeCheckRequest, nugetPackages);
                await JsonSerializer.SerializeAsync(context.Response.Body, codeCheckResults);
                return;
            }
            else if (context.Request.Path.Value?.EndsWith("format") == true)
            {
                var codeFormatRequest = JsonSerializer.Deserialize<CodeFormatRequest>(text);
                var codeCheckResults = CompletitionRequestHandler.FormatCode(codeFormatRequest?.SourceCode);
                await JsonSerializer.SerializeAsync(context.Response.Body, new
                {
                    code = 0,
                    data = codeCheckResults
                });
                return;
            }
            else if (context.Request.Path.Value?.EndsWith("run") == true)
            {
                var codeRunRequest = JsonSerializer.Deserialize<CodeRunRequest>(text);
                string nugetPackages = string.Join(" ", codeRunRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);

                // 设置响应头，允许流式输出
                context.Response.Headers.TryAdd("Content-Type", "text/event-stream");
                context.Response.Headers.TryAdd("Cache-Control", "no-cache");
                context.Response.Headers.TryAdd("Connection", "keep-alive");

                // 使用CancellationTokenSource来控制任务的取消
                var cts = new CancellationTokenSource();
                context.RequestAborted.Register(() => cts.Cancel());

                // 创建一个信号量来同步写入操作
                var writeLock = new SemaphoreSlim(1, 1);

                // 创建输出和错误回调
                async Task OnOutput(string output)
                {
                    if (cts.Token.IsCancellationRequested)
                        return;

                    try
                    {
                        await writeLock.WaitAsync(cts.Token);
                        try
                        {
                            if (!context.Response.HasStarted && !cts.Token.IsCancellationRequested)
                            {
                                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "output", content = output })}\n\n", cts.Token);
                                await context.Response.Body.FlushAsync(cts.Token);
                            }
                        }
                        finally
                        {
                            writeLock.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 请求已取消，忽略
                    }
                    catch (ObjectDisposedException)
                    {
                        // 响应已处理完毕，忽略
                    }
                    catch (Exception)
                    {
                        // 其他异常，忽略
                    }
                }

                async Task OnError(string error)
                {
                    if (cts.Token.IsCancellationRequested)
                        return;

                    try
                    {
                        await writeLock.WaitAsync(cts.Token);
                        try
                        {
                            if (!context.Response.HasStarted && !cts.Token.IsCancellationRequested)
                            {
                                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", content = error })}\n\n", cts.Token);
                                await context.Response.Body.FlushAsync(cts.Token);
                            }
                        }
                        finally
                        {
                            writeLock.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 请求已取消，忽略
                    }
                    catch (ObjectDisposedException)
                    {
                        // 响应已处理完毕，忽略
                    }
                    catch (Exception)
                    {
                        // 其他异常，忽略
                    }
                }

                try
                {
                    var result = await CodeRunner.RunProgramCodeAsync(
                        codeRunRequest?.SourceCode,
                        nugetPackages,
                        codeRunRequest?.LanguageVersion ?? 2147483647,
                        async (output) => await OnOutput(output),
                        async (error) => await OnError(error)
                    );

                    // 只有在请求未取消时才发送完成消息
                    if (!cts.Token.IsCancellationRequested)
                    {
                        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "completed", result })}\n\n", cts.Token);
                        await context.Response.Body.FlushAsync(cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    // 如果运行代码过程中发生错误，尝试发送错误消息
                    if (!cts.Token.IsCancellationRequested && !context.Response.HasStarted)
                    {
                        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", content = ex.ToString() })}\n\n");
                        await context.Response.Body.FlushAsync();
                    }
                }

                return;
            }
            else if (context.Request.Path.Value?.EndsWith("addPackages") == true)
            {
                try
                {
                    var addPackagesRequest = JsonSerializer.Deserialize<AddPackagesRequest>(text);
                    string nugetPackages = string.Join(" ", addPackagesRequest?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
                    CodeRunner.DownloadPackage(nugetPackages);
                    await JsonSerializer.SerializeAsync(context.Response.Body, new
                    {
                        code = 0,
                        data = default(object)
                    });
                }
                catch (Exception ex)
                {
                    await JsonSerializer.SerializeAsync(context.Response.Body, new
                    {
                        code = 500,
                        data = default(object),
                        message = ex.ToString()
                    });
                }
                return;
            }
        }

        context.Response.StatusCode = 405;
    }
    catch (Exception ex)
    {
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            code = 500,
            data = default(object),
            message = ex.ToString()
        });
    }
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