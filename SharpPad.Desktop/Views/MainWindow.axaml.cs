using Avalonia.Controls;
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
        
        // 订阅WebView事件
        _webView = this.FindControl<WebView>("MainWebView");
        if (_webView != null)
        {
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.NavigationStarting += OnNavigationStarting;
            
            // 设置WebView预加载配置
            ConfigureWebView(_webView);
        }
    }

    private void ConfigureWebView(WebView webView)
    {
        try
        {
            // 优化WebView性能设置
            // 注意：这些设置取决于AvaloniaWebView的具体实现
            // 如果某些属性不存在，可以移除相应的配置
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebView configuration failed: {ex.Message}");
        }
    }

    private void OnNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // 页面开始加载时的处理
            viewModel.OnWebViewNavigationStarting();
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OnWebViewNavigationCompleted(e.IsSuccess);
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