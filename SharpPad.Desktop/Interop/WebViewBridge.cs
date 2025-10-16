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
        object? contextPayload = null;
        if (root.TryGetProperty("context", out var contextProperty))
        {
            try
            {
                contextPayload = JsonSerializer.Deserialize<object>(contextProperty.GetRawText());
            }
            catch (JsonException)
            {
                contextPayload = contextProperty.GetRawText();
            }
        }

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
