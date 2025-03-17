using MonacoRoslynCompletionProvider;
using MonacoRoslynCompletionProvider.Api;
using System.Text.Json;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Cors;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .AddNewtonsoftJson(); // Use Newtonsoft.Json

// 添加CORS服务
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllWithCredentials", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 添加Swagger服务
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//添加httpclient
builder.Services.AddHttpClient();

var app = builder.Build();

// 使用静态文件中间件
app.UseFileServer();

// 使用CORS中间件
app.UseCors("AllowAllWithCredentials");

// 使用Swagger中间件
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SharpPad API V1");
});

// 启用控制器路由
app.MapControllers();

app.Run();