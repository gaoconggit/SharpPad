using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaWebView;
using SharpPad.Desktop.ViewModels;
using SharpPad.Desktop.Services;
using WebViewCore.Events;
using System;
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

    private async void OnNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
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

            // Inject native file picker bridge into JavaScript
            await InjectFilePickerBridgeAsync();
        }
    }

    private async Task InjectFilePickerBridgeAsync()
    {
        if (MainWebView == null || _filePickerBridge == null)
            return;

        try
        {
            // Set up message handler for native method invocations
            MainWebView.WebMessageReceived += OnWebMessageReceived;

            // Inject JavaScript that provides native file picker interface
            var script = @"
                (function() {
                    window.isDesktopApp = true;

                    // Storage for pending promises
                    let requestId = 0;
                    const pendingRequests = {};

                    // Handle responses from native code
                    window.handleNativeResponse = function(id, result) {
                        const pending = pendingRequests[id];
                        if (pending) {
                            pending.resolve(result);
                            delete pendingRequests[id];
                        }
                    };

                    // Send message to native code
                    function sendNativeMessage(method, args) {
                        return new Promise((resolve, reject) => {
                            const id = ++requestId;
                            pendingRequests[id] = { resolve, reject };

                            const message = JSON.stringify({
                                id: id,
                                method: method,
                                args: args
                            });

                            // Use postMessage to communicate with native code
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage(message);
                            } else {
                                // Fallback: trigger a custom event that native code can listen to
                                window.postMessage(message, '*');
                            }

                            // Timeout after 30 seconds
                            setTimeout(() => {
                                if (pendingRequests[id]) {
                                    reject(new Error('Request timeout'));
                                    delete pendingRequests[id];
                                }
                            }, 30000);
                        });
                    }

                    window.nativeFilePicker = {
                        pickJsonFile: async function() {
                            return await sendNativeMessage('pickJsonFile', {});
                        },
                        saveJsonFile: async function(content, fileName) {
                            return await sendNativeMessage('saveJsonFile', {
                                content: content,
                                fileName: fileName
                            });
                        }
                    };

                    console.log('Native file picker bridge initialized');
                })();
            ";

            await MainWebView.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to inject file picker bridge: {ex.Message}");
        }
    }

    private async void OnWebMessageReceived(object? sender, WebViewCore.Events.WebMessageReceivedEventArgs e)
    {
        if (_filePickerBridge == null || string.IsNullOrEmpty(e.Message))
            return;

        try
        {
            var request = System.Text.Json.JsonSerializer.Deserialize<NativeMethodRequest>(e.Message);
            if (request == null)
                return;

            string result = request.Method switch
            {
                "pickJsonFile" => await _filePickerBridge.PickJsonFileAsync(),
                "saveJsonFile" when request.Args != null =>
                    await _filePickerBridge.SaveJsonFileAsync(
                        request.Args.Content ?? string.Empty,
                        request.Args.FileName ?? "export.json"),
                _ => System.Text.Json.JsonSerializer.Serialize(new { success = false, error = "Unknown method" })
            };

            // Send result back to JavaScript by calling the callback function
            var responseScript = $"window.handleNativeResponse({request.Id}, {result});";
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await MainWebView.ExecuteScriptAsync(responseScript);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling web message: {ex.Message}");
        }
    }

    private class NativeMethodRequest
    {
        public int Id { get; set; }
        public string? Method { get; set; }
        public MethodArgs? Args { get; set; }
    }

    private class MethodArgs
    {
        public string? Content { get; set; }
        public string? FileName { get; set; }
    }
}