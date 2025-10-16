using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SharpPad.Controllers;

[ApiController]
[Route("api/files")]
public class FileImportController : ControllerBase
{
    private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportAsync([FromForm] IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "未选择有效的文件。" });
        }

        if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "仅支持导入 JSON 文件。" });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { message = "文件大小超出限制 (2MB)。" });
        }

        string content;
        using (var stream = file.OpenReadStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            content = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new { message = "文件内容为空。" });
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                return BadRequest(new { message = "文件必须包含 JSON 对象或数组。" });
            }
        }
        catch (JsonException ex)
        {
            return BadRequest(new { message = $"JSON 解析失败: {ex.Message}" });
        }

        return Ok(new { content });
    }
}
