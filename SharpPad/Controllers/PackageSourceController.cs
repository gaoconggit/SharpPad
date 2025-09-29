using Microsoft.AspNetCore.Mvc;
using monacoEditorCSharp.DataHelpers;

namespace SharpPad.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PackageSourceController : ControllerBase
    {
        [HttpGet("list")]
        public IActionResult GetPackageSources()
        {
            try
            {
                var sources = PackageSourceManager.GetAvailableSources();
                return Ok(new
                {
                    code = 0,
                    data = sources
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    code = 500,
                    data = default(object),
                    message = ex.Message
                });
            }
        }

        [HttpGet("default")]
        public IActionResult GetDefaultSource()
        {
            try
            {
                var defaultSource = PackageSourceManager.GetDefaultSource();
                return Ok(new
                {
                    code = 0,
                    data = defaultSource
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    code = 500,
                    data = default(object),
                    message = ex.Message
                });
            }
        }

        [HttpPost("custom")]
        public IActionResult AddCustomSource([FromBody] AddCustomSourceRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.Key) || string.IsNullOrWhiteSpace(request?.Url))
                {
                    return BadRequest(new
                    {
                        code = 400,
                        data = default(object),
                        message = "Key and URL are required"
                    });
                }

                PackageSourceManager.AddCustomSource(
                    request.Key,
                    request.Name,
                    request.Url,
                    request.SearchUrl,
                    request.ApiUrl
                );

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
                    message = ex.Message
                });
            }
        }

        [HttpDelete("custom/{key}")]
        public IActionResult RemoveCustomSource(string key)
        {
            try
            {
                var success = PackageSourceManager.RemoveCustomSource(key);
                if (success)
                {
                    return Ok(new
                    {
                        code = 0,
                        data = default(object)
                    });
                }
                else
                {
                    return NotFound(new
                    {
                        code = 404,
                        data = default(object),
                        message = "Custom source not found or cannot be removed"
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    code = 500,
                    data = default(object),
                    message = ex.Message
                });
            }
        }
    }

    public class AddCustomSourceRequest
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string SearchUrl { get; set; }
        public string ApiUrl { get; set; }
    }
}