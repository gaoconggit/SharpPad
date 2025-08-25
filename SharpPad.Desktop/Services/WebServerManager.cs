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

        // 设置正确的内容根路径
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var publishedConfigPath = Path.Combine(baseDirectory, "appsettings.json");
        var publishedWwwrootPath = Path.Combine(baseDirectory, "wwwroot");
        
        string configBasePath;
        string contentRootPath;
        string webRootPath;
        
        // 检查是否在macOS应用包中（.app bundle）
        if (baseDirectory.Contains(".app/Contents/MacOS"))
        {
            // macOS应用包结构：SharpPad.app/Contents/MacOS/
            configBasePath = baseDirectory;
            contentRootPath = baseDirectory;
            webRootPath = publishedWwwrootPath;
            
            // 验证必要文件是否存在
            if (!File.Exists(publishedConfigPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {publishedConfigPath}");
            }
            if (!Directory.Exists(publishedWwwrootPath))
            {
                throw new DirectoryNotFoundException($"Web root directory not found: {publishedWwwrootPath}");
            }
        }
        else if (File.Exists(publishedConfigPath) && Directory.Exists(publishedWwwrootPath))
        {
            // 发布后的路径结构
            configBasePath = baseDirectory;
            contentRootPath = baseDirectory;
            webRootPath = publishedWwwrootPath;
        }
        else
        {
            // 开发环境路径结构
            var sharpPadProjectPath = Path.Combine(baseDirectory, "..", "..", "..", "..", "SharpPad");
            var absoluteSharpPadPath = Path.GetFullPath(sharpPadProjectPath);
            configBasePath = absoluteSharpPadPath;
            contentRootPath = absoluteSharpPadPath;
            webRootPath = Path.Combine(absoluteSharpPadPath, "wwwroot");
            
            // 验证开发环境路径
            if (!Directory.Exists(absoluteSharpPadPath))
            {
                throw new DirectoryNotFoundException($"SharpPad project directory not found: {absoluteSharpPadPath}");
            }
        }
        
        // 读取配置
        var configuration = new ConfigurationBuilder()
            .SetBasePath(configBasePath)
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

        // 检查端口是否可用，如果不可用则使用随机端口
        if (!IsPortAvailable(Port))
        {
            Port = GetAvailablePort();
            Url = $"http://localhost:{Port}";
            kestrelUrl = Url;
        }

        try
        {
            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls(kestrelUrl)
                              .UseContentRoot(contentRootPath)
                              .UseWebRoot(webRootPath)
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

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch
        {
            return false;
        }
    }
}