using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SharpPad.Desktop.Services;
using SharpPad.Desktop.ViewModels;
using SharpPad.Desktop.Views;


namespace SharpPad.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.ShutdownRequested += OnShutdownRequested;

            // 异步启动Web服务器
            _ = Task.Run(async () =>
            {
                try
                {
                    await WebServerManager.Instance.StartAsync();
                    
                    // 在UI线程上更新URL
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        viewModel.WebUrl = WebServerManager.Instance.Url;
                        viewModel.IsLoading = false;
                        viewModel.IsWebViewVisible = true;
                    });
                }
                catch (Exception ex)
                {
                    // 在UI线程上显示错误
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        viewModel.IsLoading = false;
                        viewModel.IsMacFallbackVisible = true;
                        // 可以在这里添加错误显示逻辑
                        System.Diagnostics.Debug.WriteLine($"Web server start failed: {ex.Message}");
                    });
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (WebServerManager.Instance.IsRunning)
        {
            await WebServerManager.Instance.StopAsync();
        }
    }
}