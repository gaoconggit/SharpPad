using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Threading;
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

    public MainWindowViewModel()
    {
        NewFileCommand = new RelayCommand(() => { /* TODO: Implement */ });
        OpenFileCommand = new RelayCommand(() => { /* TODO: Implement */ });
        SaveFileCommand = new RelayCommand(() => { /* TODO: Implement */ });
        ExitCommand = new RelayCommand(() => Environment.Exit(0));
        AboutCommand = new RelayCommand(() => { /* TODO: Show about dialog */ });

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // 第1步：桌面应用启动完成，显示加载界面
            LoadingStatus = "正在启动 Web 服务器...";
            ProgressWidth = 70;
            await Task.Delay(150); // 短暂显示完成状态

            // 第2步：启动Web服务器
            await WebServerManager.Instance.StartAsync();

            LoadingStatus = "Web 服务器启动完成";
            ProgressWidth = 140;
            await Task.Delay(150); // 短暂显示完成状态

            // 第3步：WebView组件初始化（透明，深色背景）
            LoadingStatus = "正在初始化 WebView 组件...";
            ProgressWidth = 210;
            await Task.Delay(150); // 短暂显示完成状态

            // 设置URL，开始导航到Web应用
            WebUrl = WebServerManager.Instance.Url;
            //// 给导航启动一点时间
            //await Task.Delay(00);

            //LoadingStatus = "加载完成!";
            //ProgressWidth = 350;
            //IsWebViewVisible = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"启动流程失败: {ex.Message}");
            LoadingStatus = "启动失败: " + ex.Message;
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