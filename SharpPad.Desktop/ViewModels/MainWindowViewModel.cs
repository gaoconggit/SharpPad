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
            // 启动Web服务器
            await WebServerManager.Instance.StartAsync();

            // 设置WebView URL - 从appsettings.json配置读取
            WebUrl = WebServerManager.Instance.Url;
            
            // 统一使用WebView
            IsWebViewVisible = true;
            IsMacFallbackVisible = false;
            
            IsLoading = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start web server: {ex.Message}");
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

    public void OnWebViewNavigationCompleted(bool isSuccess)
    {
        if (isSuccess)
        {
            IsWebViewVisible = true;
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