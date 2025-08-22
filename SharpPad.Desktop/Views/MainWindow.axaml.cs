using Avalonia.Controls;
using AvaloniaWebView;
using SharpPad.Desktop.ViewModels;
using WebViewCore.Events;

namespace SharpPad.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // 订阅WebView事件
        var webView = this.FindControl<WebView>("MainWebView");
        if (webView != null)
        {
            webView.NavigationCompleted += OnNavigationCompleted;
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OnWebViewNavigationCompleted(e.IsSuccess);
        }
    }
}