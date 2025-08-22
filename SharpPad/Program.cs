using MonacoRoslynCompletionProvider;
using MonacoRoslynCompletionProvider.Api;
using System.Text.Json;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Cors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace SharpPad
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            
            // 配置服务
            ConfigureServices(builder.Services);
            
            var app = builder.Build();
            
            // 配置管道
            Configure(app);
            
            app.Run();
        }
        
        public static void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers()
                .AddNewtonsoftJson(); // Use Newtonsoft.Json

            // 添加CORS服务
            services.AddCors(options =>
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
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();

            //添加httpclient
            services.AddHttpClient();
        }
        
        public static void Configure(WebApplication app)
        {
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
        }
        
        // 为Desktop项目提供的IApplicationBuilder版本
        public static void Configure(IApplicationBuilder app)
        {
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

            // 启用路由
            app.UseRouting();
            
            // 启用控制器端点
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}