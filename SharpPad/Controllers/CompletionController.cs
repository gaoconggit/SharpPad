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
        public async Task<IActionResult> CodeCheck([FromBody] MultiFileCodeCheckRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);

            if (request?.IsMultiFile == true)
            {
                var codeCheckResults = await MonacoRequestHandler.MultiFileCodeCheckHandle(request, nugetPackages);
                return Ok(codeCheckResults);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new CodeCheckRequest
                {
                    Code = request?.Code,
                    Packages = request?.Packages
                };
                var codeCheckResults = await MonacoRequestHandler.CodeCheckHandle(singleRequest, nugetPackages);
                return Ok(codeCheckResults);
            }
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

        [HttpPost("definition")]
        public async Task<IActionResult> Definition([FromBody] DefinitionRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var definitionResult = await MonacoRequestHandler.DefinitionHandle(request, nugetPackages);
            return Ok(definitionResult);
        }

        [HttpPost("semanticTokens")]
        public async Task<IActionResult> SemanticTokens([FromBody] SemanticTokensRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
            var semanticTokensResult = await MonacoRequestHandler.SemanticTokensHandle(request, nugetPackages);
            return Ok(semanticTokensResult);
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

        [HttpPost("codeActions")]
        public async Task<IActionResult> CodeActions([FromBody] CodeActionRequest request)
        {
            try
            {
                string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);
                var codeActionResults = await MonacoRequestHandler.CodeActionsHandle(request, nugetPackages);
                return Ok(codeActionResults);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    code = 500,
                    data = Array.Empty<CodeActionResult>(),
                    message = ex.Message
                });
            }
        }

        [HttpGet("testXmlDoc")]
        public IActionResult TestXmlDocumentation()
        {
            var result = MonacoRoslynCompletionProvider.CompletionWorkspace.TestXmlDocumentationLoading();
            MonacoRoslynCompletionProvider.CompletionWorkspace.ClearReferenceCache(); // 清理缓存以应用新的逻辑
            return Ok(new { message = result });
        }

        [HttpGet("testCoreLibXmlDoc")]
        public IActionResult TestCoreLibXmlDocumentation()
        {
            var result = MonacoRoslynCompletionProvider.CompletionWorkspace.TestXmlDocumentationLoading("System.Private.CoreLib");
            MonacoRoslynCompletionProvider.CompletionWorkspace.ClearReferenceCache(); // 清理缓存以应用新的逻辑
            return Ok(new { message = result });
        }

        // Multi-file support endpoints
        [HttpPost("multiFileComplete")]
        public async Task<IActionResult> MultiFileComplete([FromBody] MultiFileTabCompletionRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);

            if (request?.IsMultiFile == true)
            {
                var tabCompletionResults = await MonacoRequestHandler.MultiFileCompletionHandle(request, nugetPackages);
                return Ok(tabCompletionResults);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new TabCompletionRequest
                {
                    Code = request?.Code,
                    Position = request?.Position ?? 0,
                    Packages = request?.Packages
                };
                var tabCompletionResults = await MonacoRequestHandler.CompletionHandle(singleRequest, nugetPackages);
                return Ok(tabCompletionResults);
            }
        }

        [HttpPost("multiFileCodeCheck")]
        public async Task<IActionResult> MultiFileCodeCheck([FromBody] MultiFileCodeCheckRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);

            if (request?.IsMultiFile == true)
            {
                var codeCheckResults = await MonacoRequestHandler.MultiFileCodeCheckHandle(request, nugetPackages);
                return Ok(codeCheckResults);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new CodeCheckRequest
                {
                    Code = request?.Code,
                    Packages = request?.Packages
                };
                var codeCheckResults = await MonacoRequestHandler.CodeCheckHandle(singleRequest, nugetPackages);
                return Ok(codeCheckResults);
            }
        }

        [HttpPost("multiFileSignature")]
        public async Task<IActionResult> MultiFileSignature([FromBody] MultiFileSignatureHelpRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);

            if (request?.IsMultiFile == true)
            {
                var signatureHelpResult = await MonacoRequestHandler.MultiFileSignatureHelpHandle(request, nugetPackages);
                return Ok(signatureHelpResult);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new SignatureHelpRequest
                {
                    Code = request?.Code,
                    Position = request?.Position ?? 0,
                    Packages = request?.Packages
                };
                var signatureHelpResult = await MonacoRequestHandler.SignatureHelpHandle(singleRequest, nugetPackages);
                return Ok(signatureHelpResult);
            }
        }

        [HttpPost("multiFileHover")]
        public async Task<IActionResult> MultiFileHover([FromBody] MultiFileHoverInfoRequest request)
        {
            string nugetPackages = string.Join(" ", request?.Packages.Select(p => $"{p.Id},{p.Version};{Environment.NewLine}") ?? []);

            if (request?.IsMultiFile == true)
            {
                var hoverInfoResult = await MonacoRequestHandler.MultiFileHoverHandle(request, nugetPackages);
                return Ok(hoverInfoResult);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new HoverInfoRequest
                {
                    Code = request?.Code,
                    Position = request?.Position ?? 0,
                    Packages = request?.Packages
                };
                var hoverInfoResult = await MonacoRequestHandler.HoverHandle(singleRequest, nugetPackages);
                return Ok(hoverInfoResult);
            }
        }
    }
} 