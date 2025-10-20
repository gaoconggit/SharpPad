using Avalonia.Controls;
using AvaloniaWebView;
using SharpPad.Desktop.Interop;
using SharpPad.Desktop.ViewModels;
using WebViewCore.Events;
using System;
using WebViewCore.Enums;

namespace SharpPad.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly WebViewBridge _bridge;

    public MainWindow()
    {
        InitializeComponent();
        _bridge = new WebViewBridge(MainWebView, this);
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
        }

        _bridge.NotifyHostReady();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _bridge.Dispose();
    }

    private void OnNewWindowRequested(object? sender, WebViewNewWindowEventArgs e)
    {
        // 获取当前应用的基础URL
        if (DataContext is MainWindowViewModel viewModel)
        {
            var appBaseUrl = viewModel.WebUrl;
            var navigationUrl = e.Url?.ToString() ?? string.Empty;

            // 如果导航URL不是空的，并且不是应用的基础URL（或其子路径）
            if (!string.IsNullOrEmpty(navigationUrl) &&
                !string.IsNullOrEmpty(appBaseUrl) &&
                Uri.TryCreate(appBaseUrl, UriKind.Absolute, out var appUri) &&
                Uri.TryCreate(navigationUrl, UriKind.Absolute, out var navUri))
            {
                // 如果是外部链接（不同的host或scheme）
                if (navUri.Host != appUri.Host || navUri.Scheme != appUri.Scheme)
                {
                    // 取消WebView导航
                    if (Uri.TryCreate(appBaseUrl, UriKind.Absolute, out var tempAppUri))
                    {
                        e.Url = tempAppUri;
                        e.UrlLoadingStrategy = UrlRequestStrategy.OpenExternally;
                    }
                }
            }
        }
    }
}