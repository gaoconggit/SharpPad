using Avalonia;
using System.Runtime.InteropServices;
using Avalonia.WebView.Desktop;

namespace SharpPad.Desktop;

internal class Program
{
#if WINDOWS
    [STAThread]
#endif
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