using Avalonia;
using Avalonia.WebView.Desktop;
using AvaloniaWebView;

namespace SharpPad.Desktop;

internal class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            //.WithInterFont()
            .LogToTrace()
            .UseDesktopWebView();
}