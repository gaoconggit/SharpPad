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
            Response.Headers.TryAdd("Content-Type", "text/event-stream;charset=utf-8");
            Response.Headers.TryAdd("Cache-Control", "no-cache");
            Response.Headers.TryAdd("Connection", "keep-alive");

            var cts = new CancellationTokenSource();
            HttpContext.RequestAborted.Register(() => cts.Cancel());

            // 创建一个无界的 Channel 以缓冲输出
            var channel = Channel.CreateUnbounded<string>();

            try
            {
                // 创建处理 Channel 的任务
                var processTask = ProcessChannelAsync(channel.Reader, cts.Token);

                // 执行代码运行器
                var result = await CodeRunner.RunProgramCodeAsync(
                    request?.SourceCode,
                    nugetPackages,
                    request?.LanguageVersion ?? 2147483647,
                    message => OnOutputAsync(message, channel.Writer, cts.Token),
                    error => OnErrorAsync(error, channel.Writer, cts.Token)
                );

                // 发送完成消息
                await channel.Writer.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "completed", result })}\n\n", cts.Token);

                // 关闭 Channel，不再接受新消息
                channel.Writer.Complete();

                // 等待处理任务完成
                await processTask;
            }
            catch (Exception ex)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", content = ex.ToString() })}\n\n", cts.Token);
                        await Response.Body.FlushAsync(cts.Token);
                    }
                    catch
                    {
                        // 如果响应已经被处理，则忽略该异常
                    }
                }
            }
        }

        private async Task OnOutputAsync(string output, ChannelWriter<string> writer, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                await writer.WriteAsync(
                    $"data: {JsonSerializer.Serialize(new { type = "output", content = output })}\n\n",
                    token);
            }
            catch (ChannelClosedException)
            {
                // Channel 已关闭，可以选择记录日志或忽略此异常
            }
        }

        private async Task OnErrorAsync(string error, ChannelWriter<string> writer, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                await writer.WriteAsync(
                    $"data: {JsonSerializer.Serialize(new { type = "error", content = error })}\n\n",
                    token);
            }
            catch (ChannelClosedException)
            {
                // Channel 已关闭，可以选择记录日志或忽略此异常
            }
        }

        private async Task ProcessChannelAsync(ChannelReader<string> reader, CancellationToken token)
        {
            try
            {
                await foreach (var message in reader.ReadAllAsync(token))
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        await Response.WriteAsync(message, token);
                        await Response.Body.FlushAsync(token);
                    }
                    catch (ObjectDisposedException ex)
                    {
                        // 记录日志并退出，避免继续尝试写入已释放的响应
                        Console.WriteLine($"Response 对象已释放: {ex.Message}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Response 写入异常: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理 Channel 时发生异常: {ex.Message}");
            }
        }
    }
}