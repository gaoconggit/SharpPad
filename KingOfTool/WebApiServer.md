```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;
using System.Net;
using System.Net.Sockets;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder();

        // 禁用默认配置
        builder.Configuration.Sources.Clear();

        //注入当前程序集
        var myAssembly = typeof(Program).Assembly;
        // 添加控制器服务
        builder.Services.AddControllers()
            .AddApplicationPart(myAssembly);

        // 添加Swagger服务
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.MapGet("/", () => "Hello World!");

        app.MapGet("/exit", (IHostApplicationLifetime appLifetime) =>
        {
            appLifetime.StopApplication();
            return "Goodbye, World!";
        });

        // 使用Swagger中间件
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
        });

        app.MapControllers();

        int port = GetAvailablePort();
        $"注意：退出程序访问 http://localhost:{port}/exit".Dump();
        await app.RunAsync($"http://localhost:{port}");
    }

    public static int GetAvailablePort()
    {
        // 尝试从 1024 到 65535 之间查找可用端口
        for (int port = 1024; port <= 65535; port++)
        {
            try
            {
                // 创建一个 TcpListener 实例，尝试绑定到指定端口
                TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start(); // 尝试启动监听
                listener.Stop();  // 成功启动后停止监听，表示端口可用
                return port; // 如果没有抛出异常，说明端口可用，返回该端口
            }
            catch (SocketException)
            {
                // 如果端口被占用，会抛出 SocketException，我们继续尝试下一个端口
                continue;
            }
        }

        throw new InvalidOperationException("No available port found.");
    }
}

[ApiController]
[Route("api/[controller]/[action]")]
public class TestController : ControllerBase
{
    private readonly ILogger<TestController> _logger;

    public TestController(ILogger<TestController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            Date = DateTime.Now,
            Temperature = 25,
            Summary = "Sunny"
        });
    }
}
```

