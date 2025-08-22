using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpPad.Dto;

namespace SharpPad.Controllers
{
    [ApiController]
    [Route("/v1/chat/")]
    public class ChatController : ControllerBase
    {
        private readonly ILogger<ChatController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private const int BufferSize = 81920; // 80KB buffer for optimal streaming

        public ChatController(ILogger<ChatController> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost("completions")]
        public async Task Chat([FromBody] object requestBody, CancellationToken cancellationToken = default)
        {
            // Get endpoint from X-Endpoint header
            if (!Request.Headers.TryGetValue("X-Endpoint", out var endpointHeader) || string.IsNullOrEmpty(endpointHeader))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("X-Endpoint header is required", cancellationToken);
                return;
            }

            string endpoint = endpointHeader.ToString();

            try
            {
                Response.Headers.TryAdd("Content-Type", "text/event-stream;charset=utf-8");
                Response.Headers.TryAdd("Cache-Control", "no-cache");
                Response.Headers.TryAdd("Connection", "keep-alive");

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(requestBody?.ToString() ?? "", Encoding.UTF8, "application/json")
                };

                if (Request.Headers.TryGetValue("Authorization", out var authHeader))
                {
                    string token = Regex.Replace(authHeader.ToString(), "^Bearer\\s+", "", RegexOptions.IgnoreCase);
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                // Reuse HttpClient from factory
                var httpClient = _httpClientFactory.CreateClient();

                // Use ResponseHeadersRead to start streaming as soon as headers are available
                using var response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                Response.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Chat completion request failed with status code {StatusCode}", response.StatusCode);
                    return;
                }

                // Stream the response efficiently
                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                byte[] buffer = new byte[BufferSize];
                int bytesRead;

                while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await Response.Body.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Request was canceled, no need to do anything
                _logger.LogInformation("Chat completion request was canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during chat completion");

                if (!Response.HasStarted)
                {
                    Response.StatusCode = 500;
                    await Response.WriteAsync("An error occurred while processing your request", cancellationToken);
                }
            }
        }
    }
}