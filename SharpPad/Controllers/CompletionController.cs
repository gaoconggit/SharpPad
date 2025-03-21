using Microsoft.AspNetCore.Mvc;
using MonacoRoslynCompletionProvider;
using MonacoRoslynCompletionProvider.Api;
using System.Text.Json;

namespace SharpPad.Controllers
{
    [ApiController]
    [Route("/[controller]")]
    public class CompletionController : ControllerBase
    {
        [HttpPost("complete")]
        public async Task<IActionResult> Complete([FromBody] TabCompletionRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var tabCompletionResults = await MonacoRequestHandler.CompletionHandle(request, nugetPackages);
            return Ok(tabCompletionResults);
        }

        [HttpPost("signature")]
        public async Task<IActionResult> Signature([FromBody] SignatureHelpRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var signatureHelpResult = await MonacoRequestHandler.SignatureHelpHandle(request, nugetPackages);
            return Ok(signatureHelpResult);
        }

        [HttpPost("hover")]
        public async Task<IActionResult> Hover([FromBody] HoverInfoRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var hoverInfoResult = await MonacoRequestHandler.HoverHandle(request, nugetPackages);
            return Ok(hoverInfoResult);
        }

        [HttpPost("codeCheck")]
        public async Task<IActionResult> CodeCheck([FromBody] CodeCheckRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var codeCheckResults = await MonacoRequestHandler.CodeCheckHandle(request, nugetPackages);
            return Ok(codeCheckResults);
        }

        [HttpPost("format")]
        public IActionResult Format([FromBody] CodeFormatRequest request)
        {
            var codeCheckResults = MonacoRequestHandler.FormatCode(request?.SourceCode);
            return Ok(new
            {
                code = 0,
                data = codeCheckResults
            });
        }

        [HttpPost("addPackages")]
        public IActionResult AddPackages([FromBody] AddPackagesRequest request)
        {
            try
            {
                string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
                CodeRunner.DownloadPackage(nugetPackages);
                return Ok(new
                {
                    code = 0,
                    data = default(object)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    code = 500,
                    data = default(object),
                    message = ex.ToString()
                });
            }
        }
    }
} 