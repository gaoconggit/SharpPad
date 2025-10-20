using Microsoft.AspNetCore.Mvc;
using MonacoRoslynCompletionProvider;
using MonacoRoslynCompletionProvider.Api;
using System;
using System.Collections.Generic;
using System.Linq;
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
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }
            var tabCompletionResults = await MonacoRequestHandler.CompletionHandle(request, nugetPackages, request?.ProjectType);
            return Ok(tabCompletionResults);
        }

        [HttpPost("signature")]
        public async Task<IActionResult> Signature([FromBody] SignatureHelpRequest request)
        {
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }
            var signatureHelpResult = await MonacoRequestHandler.SignatureHelpHandle(request, nugetPackages, request?.ProjectType);
            return Ok(signatureHelpResult);
        }

        [HttpPost("hover")]
        public async Task<IActionResult> Hover([FromBody] HoverInfoRequest request)
        {
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }
            var hoverInfoResult = await MonacoRequestHandler.HoverHandle(request, nugetPackages, request?.ProjectType);
            return Ok(hoverInfoResult);
        }

        [HttpPost("codeCheck")]
        public async Task<IActionResult> CodeCheck([FromBody] MultiFileCodeCheckRequest request)
        {
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }

            if (request?.IsMultiFile == true)
            {
                var codeCheckResults = await MonacoRequestHandler.MultiFileCodeCheckHandle(request, nugetPackages, request?.ProjectType);
                return Ok(codeCheckResults);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new CodeCheckRequest
                {
                    Code = request?.Code,
                    Packages = packages,
                    ProjectType = request?.ProjectType,
                };
                var codeCheckResults = await MonacoRequestHandler.CodeCheckHandle(singleRequest, nugetPackages, request?.ProjectType);
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
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }
            var definitionResult = await MonacoRequestHandler.DefinitionHandle(request, nugetPackages, request?.ProjectType);
            return Ok(definitionResult);
        }

        [HttpPost("semanticTokens")]
        public async Task<IActionResult> SemanticTokens([FromBody] SemanticTokensRequest request)
        {
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }
            var semanticTokensResult = await MonacoRequestHandler.SemanticTokensHandle(request, nugetPackages, request?.ProjectType);
            return Ok(semanticTokensResult);
        }

        [HttpPost("multiFileSemanticTokens")]
        public async Task<IActionResult> MultiFileSemanticTokens([FromBody] MultiFileSemanticTokensRequest request)
        {
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }

            if (request?.IsMultiFile == true)
            {
                var semanticTokensResult = await MonacoRequestHandler.MultiFileSemanticTokensHandle(request, nugetPackages, request?.ProjectType);
                return Ok(semanticTokensResult);
            }
            else
            {
                var singleRequest = new SemanticTokensRequest
                {
                    Code = request?.Code ?? string.Empty,
                    Packages = packages,
                    ProjectType = request?.ProjectType
                };

                var semanticTokensResult = await MonacoRequestHandler.SemanticTokensHandle(singleRequest, nugetPackages, request?.ProjectType);
                return Ok(semanticTokensResult);
            }
        }

        [HttpPost("addPackages")]
        public IActionResult AddPackages([FromBody] AddPackagesRequest request)
        {
            try
            {
                var (_, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType, ensureDownloaded: false);
                CodeRunner.DownloadPackage(nugetPackages, request?.SourceKey);
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

        [HttpPost("removePackages")]
        public IActionResult RemovePackages([FromBody] RemovePackagesRequest request)
        {
            try
            {
                var (_, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType, ensureDownloaded: false);
                CodeRunner.RemovePackages(nugetPackages);
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
                var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
                if (request != null)
                {
                    request.Packages = packages;
                }

                var codeActionResults = await MonacoRequestHandler.CodeActionsHandle(request, nugetPackages, request?.ProjectType);
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
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }

            if (request?.IsMultiFile == true)
            {
                var tabCompletionResults = await MonacoRequestHandler.MultiFileCompletionHandle(request, nugetPackages, request?.ProjectType);
                return Ok(tabCompletionResults);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new TabCompletionRequest
                {
                    Code = request?.Code,
                    Position = request?.Position ?? 0,
                    Packages = packages,
                    ProjectType = request?.ProjectType,
                };
                var tabCompletionResults = await MonacoRequestHandler.CompletionHandle(singleRequest, nugetPackages, request?.ProjectType);
                return Ok(tabCompletionResults);
            }
        }

        [HttpPost("multiFileCodeCheck")]
        public async Task<IActionResult> MultiFileCodeCheck([FromBody] MultiFileCodeCheckRequest request)
        {
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }

            if (request?.IsMultiFile == true)
            {
                var codeCheckResults = await MonacoRequestHandler.MultiFileCodeCheckHandle(request, nugetPackages, request?.ProjectType);
                return Ok(codeCheckResults);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new CodeCheckRequest
                {
                    Code = request?.Code,
                    Packages = packages,
                    ProjectType = request?.ProjectType
                };
                var codeCheckResults = await MonacoRequestHandler.CodeCheckHandle(singleRequest, nugetPackages, request?.ProjectType);
                return Ok(codeCheckResults);
            }
        }

        [HttpPost("multiFileSignature")]
        public async Task<IActionResult> MultiFileSignature([FromBody] MultiFileSignatureHelpRequest request)
        {
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }

            if (request?.IsMultiFile == true)
            {
                var signatureHelpResult = await MonacoRequestHandler.MultiFileSignatureHelpHandle(request, nugetPackages, request?.ProjectType);
                return Ok(signatureHelpResult);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new SignatureHelpRequest
                {
                    Code = request?.Code,
                    Position = request?.Position ?? 0,
                    Packages = packages,
                    ProjectType = request?.ProjectType
                };
                var signatureHelpResult = await MonacoRequestHandler.SignatureHelpHandle(singleRequest, nugetPackages, request?.ProjectType);
                return Ok(signatureHelpResult);
            }
        }

        [HttpPost("multiFileHover")]
        public async Task<IActionResult> MultiFileHover([FromBody] MultiFileHoverInfoRequest request)
        {
            var (packages, nugetPackages) = PreparePackages(request?.Packages ?? Enumerable.Empty<Package>(), request?.ProjectType);
            if (request != null)
            {
                request.Packages = packages;
            }

            if (request?.IsMultiFile == true)
            {
                var hoverInfoResult = await MonacoRequestHandler.MultiFileHoverHandle(request, nugetPackages, request?.ProjectType);
                return Ok(hoverInfoResult);
            }
            else
            {
                // Backward compatibility for single file requests
                var singleRequest = new HoverInfoRequest
                {
                    Code = request?.Code,
                    Position = request?.Position ?? 0,
                    Packages = packages,
                    ProjectType = request?.ProjectType
                };
                var hoverInfoResult = await MonacoRequestHandler.HoverHandle(singleRequest, nugetPackages, request?.ProjectType);
                return Ok(hoverInfoResult);
            }
        }

        private static (List<Package> Packages, string Specification) PreparePackages(IEnumerable<Package> packages, string projectType, bool ensureDownloaded = true)
        {
            var (resolved, specification) = CodeRunner.PreparePackageReferences(packages, projectType);

            if (ensureDownloaded && !string.IsNullOrWhiteSpace(specification))
            {
                CodeRunner.DownloadPackage(specification);
            }

            return (resolved, specification);
        }
    }
}
