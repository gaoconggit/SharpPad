using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaWebView;
using SharpPad.Desktop.ViewModels;
using WebViewCore.Events;
using System;
using System.Threading.Tasks;

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
        }
    }
}