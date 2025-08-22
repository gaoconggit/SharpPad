# SharpPad Desktop Build Script for Windows and macOS
param(
    [string]$Configuration = "Release",
    [string]$Platform = "All", # All, Windows, macOS
    [switch]$SelfContained = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Building SharpPad Desktop" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platform: $Platform" -ForegroundColor Yellow

# 确保在正确的目录
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptPath

# 构建参数
$BuildArgs = @(
    "build"
    "SharpPad.Desktop/SharpPad.Desktop.csproj"
    "-c", $Configuration
    "--verbosity", "minimal"
)

if ($SelfContained) {
    $BuildArgs += "--self-contained"
}

# 构建 Windows 版本
if ($Platform -eq "All" -or $Platform -eq "Windows") {
    Write-Host "Building Windows version..." -ForegroundColor Blue
    
    $WinArgs = $BuildArgs + @("-r", "win-x64")
    & dotnet @WinArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Windows build failed"
    }
    
    Write-Host "Windows build completed" -ForegroundColor Green
}

# 构建 macOS 版本
if ($Platform -eq "All" -or $Platform -eq "macOS") {
    Write-Host "Building macOS version..." -ForegroundColor Blue
    
    $MacArgs = $BuildArgs + @("-r", "osx-x64")
    & dotnet @MacArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "macOS build failed"
    }
    
    # 如果在 macOS 上构建，创建 .app 包
    if ($env:OS -eq "Darwin") {
        Write-Host "Creating macOS .app bundle..." -ForegroundColor Blue
        # TODO: 实现 .app 包创建逻辑
    }
    
    Write-Host "macOS build completed" -ForegroundColor Green
}

Write-Host "Build process completed!" -ForegroundColor Green