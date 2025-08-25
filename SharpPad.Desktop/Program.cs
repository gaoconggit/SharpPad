using Avalonia;
using System.Runtime.InteropServices;
using Avalonia.WebView.Desktop;

namespace SharpPad.Desktop;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 在Mac上设置兼容性环境变量
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Environment.SetEnvironmentVariable("OBJC_DISABLE_INITIALIZE_FORK_SAFETY", "YES");
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "false");
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER", "false");
        }
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseDesktopWebView(); // 统一使用桌面WebView
}