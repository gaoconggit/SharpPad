using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SharpPad.Desktop.Services;

public sealed class WebServerManager
{
    private static readonly Lazy<WebServerManager> _instance = new(() => new WebServerManager());
    public static WebServerManager Instance => _instance.Value;

    private IHost? _webHost;
    private readonly object _lock = new();

    public int Port { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public bool IsRunning => _webHost != null;

    private WebServerManager() { }

    public async Task StartAsync()
    {
        lock (_lock)
        {
            if (IsRunning)
                return;
        }

        // 设置正确的内容根路径指向SharpPad项目
        var sharpPadProjectPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "SharpPad");
        var absoluteSharpPadPath = Path.GetFullPath(sharpPadProjectPath);
        
        // 从SharpPad项目的appsettings.json读取配置
        var configuration = new ConfigurationBuilder()
            .SetBasePath(absoluteSharpPadPath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
            
        var kestrelUrl = configuration["Kestrel:Endpoints:Http:Url"] ?? "http://localhost:5090";
        Url = kestrelUrl.Replace("0.0.0.0", "localhost"); // WebView使用localhost
        
        // 解析端口号
        if (Uri.TryCreate(kestrelUrl, UriKind.Absolute, out var uri))
        {
            Port = uri.Port;
        }
        else
        {
            Port = 5090; // 默认端口
        }

        try
        {
            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls(kestrelUrl)
                              .UseContentRoot(absoluteSharpPadPath)
                              .UseWebRoot(Path.Combine(absoluteSharpPadPath, "wwwroot"))
                              .ConfigureServices(SharpPad.Program.ConfigureServices)
                              .Configure(SharpPad.Program.Configure)
                              .ConfigureLogging(logging =>
                              {
                                  logging.SetMinimumLevel(LogLevel.Warning);
                              });
                });

            _webHost = builder.Build();
            await _webHost.StartAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start web server on port {Port}: {ex.Message}", ex);
        }
    }

    public async Task StopAsync()
    {
        if (_webHost != null)
        {
            await _webHost.StopAsync();
            _webHost.Dispose();
            _webHost = null;
        }
    }

    private static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}