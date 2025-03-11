using Microsoft.AspNetCore.Mvc;
using MonacoRoslynCompletionProvider;
using MonacoRoslynCompletionProvider.Api;
using System.Text.Json;

namespace SharpPad.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CodeRunController : ControllerBase
    {
        [HttpPost("run")]
        public async Task Run([FromBody] CodeRunRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);

            // 设置响应头，允许流式输出
            Response.Headers.TryAdd("Content-Type", "text/event-stream");
            Response.Headers.TryAdd("Cache-Control", "no-cache");
            Response.Headers.TryAdd("Connection", "keep-alive");

            // 使用CancellationTokenSource来控制任务的取消
            var cts = new CancellationTokenSource();
            HttpContext.RequestAborted.Register(() => cts.Cancel());

            // 创建输出和错误回调
            // 创建输出和错误回调
            async void OnOutput(string output)
            {
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "output", content = output })}\n\n");
                await Response.Body.FlushAsync();
            }

            async void OnError(string error)
            {
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", content = error })}\n\n");
                await Response.Body.FlushAsync();
            }

            try
            {
                var result = await CodeRunner.RunProgramCodeAsync(
                    request?.SourceCode,
                    nugetPackages,
                    request?.LanguageVersion ?? 2147483647,
                    OnOutput,
                    OnError
                );

                // 只有在请求未取消时才发送完成消息
                if (!cts.Token.IsCancellationRequested)
                {
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "completed", result })}\n\n", cts.Token);
                    await Response.Body.FlushAsync(cts.Token);
                }
            }
            catch (Exception ex)
            {
                // 如果运行代码过程中发生错误，尝试发送错误消息
                if (!cts.Token.IsCancellationRequested && !Response.HasStarted)
                {
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", content = ex.ToString() })}\n\n");
                    await Response.Body.FlushAsync();
                }
            }
        }
    }
}