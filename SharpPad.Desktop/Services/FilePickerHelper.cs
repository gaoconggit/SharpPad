using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SharpPad.Desktop.Services;

internal static class FilePickerHelper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB

    public static async Task<FileUploadResult> PickAndUploadAsync(Window owner, Uri? uploadEndpoint)
    {
        if (owner is null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var storageProvider = owner.StorageProvider;
        if (storageProvider is null)
        {
            return FileUploadResult.CreateFailure("当前窗口未提供存储服务。");
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要上传的文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON 文件")
                {
                    Patterns = new[] { "*.json" }
                }
            }
        });

        var file = files is { Count: > 0 } ? files[0] : null;
        if (file is null)
        {
            return FileUploadResult.CreateCancelled();
        }

        var fileName = string.IsNullOrWhiteSpace(file.Name) ? "import.json" : file.Name;

        if (!string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return FileUploadResult.CreateFailure("请选择 JSON 格式的文件。");
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
            if (stream.CanSeek && stream.Length > MaxFileSizeBytes)
            {
                return FileUploadResult.CreateFailure("文件大小超过 2MB 限制。");
            }

            if (uploadEndpoint is null)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var content = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(content))
                {
                    return FileUploadResult.CreateFailure("文件内容为空。");
                }

                if (!IsValidJson(content, out var validationError))
                {
                    return FileUploadResult.CreateFailure(validationError ?? "文件不是有效的 JSON。");
                }

                return FileUploadResult.CreateSuccess(fileName, content);
            }

            using var contentMultipart = new MultipartFormDataContent();
            contentMultipart.Add(new StreamContent(stream), "file", fileName);

            using var http = new HttpClient { Timeout = DefaultTimeout };
            using var response = await http.PostAsync(uploadEndpoint, contentMultipart);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var payload = TryExtractPayload(responseBody);
            return FileUploadResult.CreateSuccess(fileName, payload);
        }
        catch (TaskCanceledException)
        {
            return FileUploadResult.CreateFailure("上传超时，请稍后重试。");
        }
        catch (HttpRequestException ex)
        {
            return FileUploadResult.CreateFailure($"上传失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return FileUploadResult.CreateFailure(ex.Message);
        }
    }

    private static bool IsValidJson(string content, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                error = "JSON 根节点必须是对象或数组。";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON 解析失败: {ex.Message}";
            return false;
        }
    }

    private static string? TryExtractPayload(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("content", out var contentProperty) &&
                contentProperty.ValueKind == JsonValueKind.String)
            {
                return contentProperty.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore parse error, fall back to raw response
        }

        return responseBody;
    }
}

internal readonly record struct FileUploadResult(bool Success, bool Cancelled, string? FileName, string? Payload, string? Error)
{
    public static FileUploadResult CreateSuccess(string? fileName, string? payload) => new(true, false, fileName, payload, null);
    public static FileUploadResult CreateCancelled() => new(false, true, null, null, null);
    public static FileUploadResult CreateFailure(string? error) => new(false, false, null, null, error);
}
