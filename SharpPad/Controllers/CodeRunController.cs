using Microsoft.AspNetCore.Mvc;
using MonacoRoslynCompletionProvider;
using MonacoRoslynCompletionProvider.Api;
using System.Text.Json;
using System.Threading.Channels;

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

            var cts = new CancellationTokenSource();
            HttpContext.RequestAborted.Register(() => cts.Cancel());

            // 创建一个无界的 Channel 以缓冲输出
            var channel = Channel.CreateUnbounded<string>();

            // 处理 Channel 任务
            async Task ProcessChannel()
            {
                await foreach (var message in channel.Reader.ReadAllAsync(cts.Token))
                {
                    if (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await Response.WriteAsync(message, cts.Token);
                            await Response.Body.FlushAsync(cts.Token);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Response 写入异常: {ex.Message}");
                        }
                    }
                }
            }

            // 在后台启动 Channel 处理任务
            _ = Task.Run(ProcessChannel, cts.Token);

            // 回调函数将数据写入 Channel
            async Task OnOutput(string output)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    await channel.Writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "output", content = output })}\n\n");
                }
            }

            async Task OnError(string error)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    await channel.Writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", content = error })}\n\n");
                }
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

                // 发送完成消息
                if (!cts.Token.IsCancellationRequested)
                {
                    await channel.Writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "completed", result })}\n\n");
                }
            }
            catch (Exception ex)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    await channel.Writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", content = ex.ToString() })}\n\n");
                }
            }
            finally
            {
                // 关闭 Channel，通知 ProcessChannel 任务结束
                channel.Writer.Complete();
            }
        }
    }
}