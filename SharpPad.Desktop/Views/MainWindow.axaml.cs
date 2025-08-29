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
    private WebView? _webView;

    public MainWindow()
    {
        InitializeComponent();

        // 延迟初始化WebView，确保UI线程准备就绪
        _ = Task.Run(async () =>
        {
            await Task.Delay(100); // 短暂延迟

            Dispatcher.UIThread.Post(() =>
            {
                InitializeWebView();
            });
        });
    }

    private void InitializeWebView()
    {
        // 订阅WebView事件
        _webView = this.FindControl<WebView>("MainWebView");
        if (_webView != null)
        {
            // 设置WebView预加载配置
            //ConfigureWebView(_webView);

            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.NavigationStarting += OnNavigationStarting;



            Console.WriteLine("WebView initialized successfully");
        }
        else
        {
            Console.WriteLine("Failed to find WebView control");
        }
    }


    private void OnNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
    {
    }

    private void OnNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // 使用 Dispatcher 确保在 UI 线程上执行，并添加小延迟避免闪烁
            Dispatcher.UIThread.Post(async () =>
            {
                // 完成加载状态更新
                viewModel.LoadingStatus = "加载完成!";
                viewModel.ProgressWidth = 320;
                
                // 延迟确保页面内容完全渲染，避免显示空白内容
                await Task.Delay(200);
                
                // 先显示WebView容器，但透明度仍为0
                viewModel.IsWebViewVisible = true;
                
                // 微小延迟确保UI更新完成
                await Task.Delay(50);
                
                // 开始透明度动画（0.5秒CubicEaseOut动画）
                viewModel.WebViewOpacity = 1.0;
                
                // 等待动画完全完成后再隐藏加载界面（0.5秒动画 + 100ms缓冲）
                await Task.Delay(600);
                viewModel.IsLoading = false;
            });
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // 清理WebView事件订阅
        if (_webView != null)
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.NavigationStarting -= OnNavigationStarting;
        }

        base.OnClosed(e);
    }
}