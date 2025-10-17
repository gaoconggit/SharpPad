using System;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using AvaloniaWebView;
using SharpPad.Desktop.Services;
using WebViewCore.Events;

namespace SharpPad.Desktop.Interop;

internal sealed class WebViewBridge : IDisposable
{
    private readonly WebView _webView;
    private readonly Window _owner;
    private readonly JsonSerializerOptions _serializerOptions;
    private bool _disposed;

    public WebViewBridge(WebView webView, Window owner)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    public void NotifyHostReady()
    {
        Send(new
        {
            type = "host-ready",
            platform = Environment.OSVersion.Platform.ToString()
        });
    }

    private async void OnWebMessageReceived(object? sender, WebViewMessageReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Message))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(e.Message);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeProperty) ||
                typeProperty.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var messageType = typeProperty.GetString();
            switch (messageType)
            {
                case "ping":
                    Send(new { type = "pong" });
                    break;

                case "pick-and-upload":
                    await HandlePickAndUploadAsync(root);
                    break;

                case "download-file":
                    await HandleDownloadFileAsync(root);
                    break;

                case "open-external-url":
                    HandleOpenExternalUrl(root);
                    break;

                default:
                    Send(new
                    {
                        type = "bridge-warning",
                        message = $"未识别的消息类型: {messageType}"
                    });
                    break;
            }
        }
        catch (JsonException jsonEx)
        {
            Send(new
            {
                type = "bridge-error",
                message = $"消息解析失败: {jsonEx.Message}"
            });
        }
        catch (Exception ex)
        {
            Send(new
            {
                type = "bridge-error",
                message = ex.Message
            });
        }
    }

    private async Task HandlePickAndUploadAsync(JsonElement root)
    {
        var contextPayload = ExtractContext(root);

        Uri? uploadEndpoint = null;
        if (root.TryGetProperty("endpoint", out var endpointProperty))
        {
            if (endpointProperty.ValueKind == JsonValueKind.String)
            {
                var endpointValue = endpointProperty.GetString();
                if (!string.IsNullOrWhiteSpace(endpointValue))
                {
                    if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out uploadEndpoint))
                    {
                        Send(new
                        {
                            type = "pick-and-upload-completed",
                            success = false,
                            context = contextPayload,
                            message = "上传地址无效。"
                        });
                        return;
                    }
                }
            }
            else if (endpointProperty.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                Send(new
                {
                    type = "pick-and-upload-completed",
                    success = false,
                    context = contextPayload,
                    message = "上传地址参数无效。"
                });
                return;
            }
        }

        Send(new
        {
            type = "pick-and-upload-progress",
            context = contextPayload,
            status = "started"
        });

        var result = await FilePickerHelper.PickAndUploadAsync(_owner, uploadEndpoint);

        Send(new
        {
            type = "pick-and-upload-completed",
            success = result.Success,
            cancelled = result.Cancelled,
            fileName = result.FileName,
            payload = result.Payload,
            message = result.Error,
            context = contextPayload
        });
    }

    private async Task HandleDownloadFileAsync(JsonElement root)
    {
        var contextPayload = ExtractContext(root);

        if (!root.TryGetProperty("content", out var contentProperty) ||
            contentProperty.ValueKind != JsonValueKind.String)
        {
            Send(new
            {
                type = "download-file-completed",
                success = false,
                context = contextPayload,
                message = "导出内容无效。"
            });
            return;
        }

        var content = contentProperty.GetString() ?? string.Empty;

        var fileName = "export.json";
        if (root.TryGetProperty("fileName", out var fileNameProperty) &&
            fileNameProperty.ValueKind == JsonValueKind.String)
        {
            var providedName = fileNameProperty.GetString();
            if (!string.IsNullOrWhiteSpace(providedName))
            {
                fileName = providedName!;
            }
        }

        string? mimeType = null;
        if (root.TryGetProperty("mimeType", out var mimeTypeProperty) &&
            mimeTypeProperty.ValueKind == JsonValueKind.String)
        {
            var providedMime = mimeTypeProperty.GetString();
            if (!string.IsNullOrWhiteSpace(providedMime))
            {
                mimeType = providedMime;
            }
        }

        var isBase64 = root.TryGetProperty("isBase64", out var base64Property) &&
                       base64Property.ValueKind == JsonValueKind.True;

        var result = await FileSaveHelper.SaveAsync(_owner, fileName, content, mimeType, isBase64);

        Send(new
        {
            type = "download-file-completed",
            success = result.Success,
            cancelled = result.Cancelled,
            fileName = result.FileName,
            savedPath = result.FilePath,
            message = result.Error,
            context = contextPayload
        });
    }

    private void HandleOpenExternalUrl(JsonElement root)
    {
        if (!root.TryGetProperty("url", out var urlProperty) ||
            urlProperty.ValueKind != JsonValueKind.String)
        {
            Send(new
            {
                type = "open-external-url-result",
                success = false,
                message = "URL无效。"
            });
            return;
        }

        var url = urlProperty.GetString();
        if (string.IsNullOrWhiteSpace(url))
        {
            Send(new
            {
                type = "open-external-url-result",
                success = false,
                message = "URL为空。"
            });
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);

            Send(new
            {
                type = "open-external-url-result",
                success = true
            });
        }
        catch (Exception ex)
        {
            Send(new
            {
                type = "open-external-url-result",
                success = false,
                message = $"打开URL失败: {ex.Message}"
            });
        }
    }

    private object? ExtractContext(JsonElement root)
    {
        if (!root.TryGetProperty("context", out var contextProperty))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<object>(contextProperty.GetRawText());
        }
        catch (JsonException)
        {
            return contextProperty.GetRawText();
        }
    }

    private void Send(object payload)
    {
        if (_disposed)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, _serializerOptions);
        _webView.PostWebMessageAsJson(json, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _webView.WebMessageReceived -= OnWebMessageReceived;
        _disposed = true;
    }
}
