using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaWebView;
using SharpPad.Desktop.ViewModels;
using WebViewCore.Events;
using System;
using System.Threading.Tasks;
#if MACOS
using Foundation;
using WebKit;
using Avalonia.Platform;
using SharpPad.Desktop.Platforms.MacOS;
#endif

namespace SharpPad.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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

#if MACOS
            // Set custom UIDelegate for macOS to enable file picker support
            SetupMacOSUIDelegate();
#endif
        }
    }

#if MACOS
    private void SetupMacOSUIDelegate()
    {
        try
        {
            // Access the native WKWebView from the Avalonia WebView control
            if (MainWebView?.TryGetWebViewPlatformHandle() is IAppleWKWebViewPlatformHandle handle)
            {
                var wkWebView = NSObject.GetNSObject<WKWebView>(handle.WKWebView, false);
                if (wkWebView != null)
                {
                    // Set our custom UIDelegate that handles file picker
                    wkWebView.UIDelegate = new CustomWKUIDelegate();
                    Console.WriteLine("CustomWKUIDelegate set successfully for file picker support");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set UIDelegate: {ex.Message}");
        }
    }
#endif
}