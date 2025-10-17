using Avalonia.Controls;
using AvaloniaWebView;
using SharpPad.Desktop.Interop;
using SharpPad.Desktop.ViewModels;
using WebViewCore.Events;
using System;

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
}
