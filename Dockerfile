# 使用.NET 9 SDK作为构建镜像
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 首先复制项目文件并恢复依赖项，利用Docker缓存层
COPY ["SharpPad/SharpPad.csproj", "SharpPad/"]
COPY ["MonacoRoslynCompletionProvider/MonacoRoslynCompletionProvider.csproj", "MonacoRoslynCompletionProvider/"]
RUN dotnet restore "SharpPad/SharpPad.csproj"

# 复制源代码
COPY . .

# 构建应用程序
WORKDIR "/src/SharpPad"
RUN dotnet build "SharpPad.csproj" -c Release -o /app/build

# 发布应用程序
FROM build AS publish
RUN dotnet publish "SharpPad.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 最终镜像
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 5090

# 创建必要的目录
RUN mkdir -p /app/NugetPackages/packages

# 复制发布的应用程序
COPY --from=publish /app/publish .

# 设置容器启动命令
ENTRYPOINT ["dotnet", "SharpPad.dll"]