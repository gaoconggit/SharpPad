using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using monacoEditorCSharp.DataHelpers;
using Microsoft.Extensions.Logging;

namespace SharpPad.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NugetProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<NugetProxyController> _logger;
        private static readonly JsonSerializerOptions FlatContainerJsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        public NugetProxyController(IHttpClientFactory httpClientFactory, ILogger<NugetProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet("versions")]
        public async Task<IActionResult> GetPackageVersions([FromQuery] string packageId, [FromQuery] string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return BadRequest(new
                {
                    code = 400,
                    message = "packageId is required"
                });
            }

            var packageSource = PackageSourceManager.GetSource(sourceKey);
            if (packageSource == null || string.IsNullOrWhiteSpace(packageSource.ApiUrl))
            {
                return BadRequest(new
                {
                    code = 400,
                    message = "Invalid package source"
                });
            }

            var baseUrl = packageSource.ApiUrl.TrimEnd('/');
            var requestUri = $"{baseUrl}/{packageId.ToLowerInvariant()}/index.json";

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.ParseAdd("SharpPad/1.0 (+https://github.com/sharppad)");

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    var errorBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
                    _logger.LogWarning("NuGet versions request failed for {PackageId} ({Status}): {Body}", packageId, status, errorBody);
                    return StatusCode(status, new
                    {
                        code = status,
                        message = $"NuGet source returned HTTP {status}"
                    });
                }

                await using var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
                var payload = await JsonSerializer.DeserializeAsync<FlatContainerIndex>(
                    stream,
                    FlatContainerJsonOptions,
                    HttpContext.RequestAborted);

                return Ok(new
                {
                    code = 0,
                    data = payload?.Versions ?? Array.Empty<string>()
                });
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                return StatusCode(499, new
                {
                    code = 499,
                    message = "Request aborted"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to proxy NuGet versions for {PackageId} via {Uri}", packageId, requestUri);
                return StatusCode(502, new
                {
                    code = 502,
                    message = "Failed to reach NuGet source"
                });
            }
        }

        private sealed class FlatContainerIndex
        {
            public string[] Versions { get; set; } = Array.Empty<string>();
        }
    }
}
