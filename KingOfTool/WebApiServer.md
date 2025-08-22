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

        "注意：退出程序访问 http://localhost:3030/exit".Dump();
        await app.RunAsync("http://localhost:3030");
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
