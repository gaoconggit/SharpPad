using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaWebView;
using SharpPad.Desktop.ViewModels;
using SharpPad.Desktop.Services;
using WebViewCore.Events;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace SharpPad.Desktop.Views;

public partial class MainWindow : Window
{
    private FilePickerBridge? _filePickerBridge;

    public MainWindow()
    {
        InitializeComponent();
        _filePickerBridge = new FilePickerBridge(this);
    }

    private void OnNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
    {
    }

    private void OnNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // 完成加载状态更新
            viewModel.LoadingStatus = "加载完成!";
            viewModel.ProgressWidth = 320;

            // 先显示WebView容器，但透明度仍为0
            viewModel.IsWebViewVisible = true;

            // 开始透明度动画（0.5秒CubicEaseOut动画）
            viewModel.WebViewOpacity = 1.0;

            viewModel.IsLoading = false;

            // Inject JavaScript bridge for file operations
            InjectJavaScriptBridge();
        }
    }

    private void InjectJavaScriptBridge()
    {
        try
        {
            var webView = this.FindControl<WebView>("MainWebView");
            if (webView == null) return;

            // Register message handler
            webView.WebMessageReceived += OnWebMessageReceived;

            // Inject JavaScript API
            var script = @"
                (function() {
                    if (window.isDesktopApp) return; // Already injected

                    window.isDesktopApp = true;

                    window.nativeFilePicker = {
                        pickJsonFile: function() {
                            return new Promise((resolve, reject) => {
                                const callbackId = 'pickFile_' + Date.now() + '_' + Math.random();
                                window[callbackId] = function(result) {
                                    delete window[callbackId];
                                    if (result && result.success) {
                                        resolve(result.content);
                                    } else {
                                        resolve(null);
                                    }
                                };

                                try {
                                    window.chrome.webview.postMessage({
                                        type: 'pickJsonFile',
                                        callbackId: callbackId
                                    });
                                } catch (e) {
                                    console.error('Failed to call native file picker:', e);
                                    delete window[callbackId];
                                    resolve(null);
                                }
                            });
                        },

                        saveJsonFile: function(fileName, content) {
                            return new Promise((resolve, reject) => {
                                const callbackId = 'saveFile_' + Date.now() + '_' + Math.random();
                                window[callbackId] = function(result) {
                                    delete window[callbackId];
                                    if (result && result.success) {
                                        resolve(true);
                                    } else {
                                        resolve(false);
                                    }
                                };

                                try {
                                    window.chrome.webview.postMessage({
                                        type: 'saveJsonFile',
                                        fileName: fileName,
                                        content: content,
                                        callbackId: callbackId
                                    });
                                } catch (e) {
                                    console.error('Failed to call native file saver:', e);
                                    delete window[callbackId];
                                    resolve(false);
                                }
                            });
                        }
                    };

                    console.log('Native file picker bridge initialized');
                })();
            ";

            webView.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to inject JavaScript bridge: {ex.Message}");
        }
    }

    private async void OnWebMessageReceived(object? sender, WebViewCore.Events.WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.MessageAsJson;
            if (string.IsNullOrEmpty(message)) return;

            var json = JsonDocument.Parse(message);
            var root = json.RootElement;

            if (!root.TryGetProperty("type", out var typeElement)) return;
            var type = typeElement.GetString();

            if (!root.TryGetProperty("callbackId", out var callbackElement)) return;
            var callbackId = callbackElement.GetString();

            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(callbackId)) return;

            switch (type)
            {
                case "pickJsonFile":
                    await HandlePickJsonFileAsync(callbackId);
                    break;

                case "saveJsonFile":
                    if (root.TryGetProperty("fileName", out var fileNameElement) &&
                        root.TryGetProperty("content", out var contentElement))
                    {
                        var fileName = fileNameElement.GetString();
                        var content = contentElement.GetString();
                        if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(content))
                        {
                            await HandleSaveJsonFileAsync(callbackId, fileName, content);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error handling web message: {ex.Message}");
        }
    }

    private async Task HandlePickJsonFileAsync(string callbackId)
    {
        try
        {
            string? content = null;

            // Must run file picker on UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_filePickerBridge != null)
                {
                    content = await _filePickerBridge.PickJsonFileAsync();
                }
            });

            // Execute callback with result
            var result = content != null
                ? new { success = true, content }
                : new { success = false, content = (string?)null };

            var resultJson = JsonSerializer.Serialize(result);
            var script = $"if (window['{callbackId}']) {{ window['{callbackId}']({resultJson}); }}";

            var webView = this.FindControl<WebView>("MainWebView");
            if (webView != null)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await webView.ExecuteScriptAsync(script);
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in HandlePickJsonFileAsync: {ex.Message}");
        }
    }

    private async Task HandleSaveJsonFileAsync(string callbackId, string fileName, string content)
    {
        try
        {
            bool success = false;

            // Must run file picker on UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_filePickerBridge != null)
                {
                    success = await _filePickerBridge.SaveJsonFileAsync(fileName, content);
                }
            });

            // Execute callback with result
            var result = new { success };
            var resultJson = JsonSerializer.Serialize(result);
            var script = $"if (window['{callbackId}']) {{ window['{callbackId}']({resultJson}); }}";

            var webView = this.FindControl<WebView>("MainWebView");
            if (webView != null)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await webView.ExecuteScriptAsync(script);
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in HandleSaveJsonFileAsync: {ex.Message}");
        }
    }
}