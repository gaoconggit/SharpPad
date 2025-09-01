using Avalonia;
using System.Runtime.InteropServices;
using Avalonia.WebView.Desktop;

namespace SharpPad.Desktop;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
         => AppBuilder.Configure<App>()
             .UsePlatformDetect()
             .LogToTrace()
             .UseDesktopWebView();
}