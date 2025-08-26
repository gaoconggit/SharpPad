using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Input;
using SharpPad.Desktop.Services;

namespace SharpPad.Desktop.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private string _webUrl = string.Empty;
    private bool _isWebViewVisible = false;
    private bool _isMacFallbackVisible = false;
    private bool _isLoading = true;
    private string _loadingStatus = "正在初始化...";
    private double _progressWidth = 0;
    private double _webViewOpacity = 0;

    public string WebUrl
    {
        get => _webUrl;
        set => SetProperty(ref _webUrl, value);
    }

    public bool IsWebViewVisible
    {
        get => _isWebViewVisible;
        set => SetProperty(ref _isWebViewVisible, value);
    }

    public bool IsMacFallbackVisible
    {
        get => _isMacFallbackVisible;
        set => SetProperty(ref _isMacFallbackVisible, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string LoadingStatus
    {
        get => _loadingStatus;
        set => SetProperty(ref _loadingStatus, value);
    }

    public double ProgressWidth
    {
        get => _progressWidth;
        set => SetProperty(ref _progressWidth, value);
    }

    public double WebViewOpacity
    {
        get => _webViewOpacity;
        set => SetProperty(ref _webViewOpacity, value);
    }

    public ICommand NewFileCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand SaveFileCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand OpenInBrowserCommand { get; }

    public MainWindowViewModel()
    {
        NewFileCommand = new RelayCommand(() => { /* TODO: Implement */ });
        OpenFileCommand = new RelayCommand(() => { /* TODO: Implement */ });
        SaveFileCommand = new RelayCommand(() => { /* TODO: Implement */ });
        ExitCommand = new RelayCommand(() => Environment.Exit(0));
        AboutCommand = new RelayCommand(() => { /* TODO: Show about dialog */ });
        OpenInBrowserCommand = new RelayCommand(OpenInBrowser);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // 更新加载状态和进度
            LoadingStatus = "正在启动 Web 服务器...";
            ProgressWidth = 30;

            // 启动Web服务器
            await WebServerManager.Instance.StartAsync();

            // 设置WebView URL - 从appsettings.json配置读取
            WebUrl = WebServerManager.Instance.Url;
            
            LoadingStatus = "正在加载界面...";
            ProgressWidth = 80;
            
            // 统一使用WebView
            IsWebViewVisible = true;
            IsMacFallbackVisible = false;
            WebViewOpacity = 1; // 直接设置为可见
            
            // 短暂延迟让WebView开始加载
            await Task.Delay(100);
            
            LoadingStatus = "加载完成!";
            ProgressWidth = 320;
            
            // 短暂延迟后隐藏加载界面
            await Task.Delay(200);
            IsLoading = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start web server: {ex.Message}");
            LoadingStatus = "加载失败: " + ex.Message;
            IsLoading = false;
        }
    }

    private void OpenInBrowser()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", WebUrl);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", WebUrl);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(WebUrl) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open browser: {ex.Message}");
        }
    }

    public void OnWebViewNavigationStarting()
    {
        // 可以在这里添加导航开始的处理逻辑，暂时不修改透明度
    }

    public void OnWebViewNavigationCompleted(bool isSuccess)
    {
        if (isSuccess)
        {
            // WebView已成功加载，确保可见
            WebViewOpacity = 1;
            
            // 确保加载界面隐藏
            IsLoading = false;
        }
        else
        {
            // 加载失败，显示错误信息
            LoadingStatus = "WebView 加载失败，请尝试重启应用";
            IsLoading = true;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}