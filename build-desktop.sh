#!/bin/bash
# SharpPad Desktop Build Script for Unix-like systems

set -e

CONFIGURATION="Release"
PLATFORM="All"
SELF_CONTAINED=false

# 解析参数
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        -p|--platform)
            PLATFORM="$2"
            shift 2
            ;;
        --self-contained)
            SELF_CONTAINED=true
            shift
            ;;
        *)
            echo "Unknown option $1"
            exit 1
            ;;
    esac
done

echo "Building SharpPad Desktop"
echo "Configuration: $CONFIGURATION"
echo "Platform: $PLATFORM"

# 确保在正确的目录
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# 基础构建参数
BUILD_ARGS="build SharpPad.Desktop/SharpPad.Desktop.csproj -c $CONFIGURATION --verbosity minimal"

if [ "$SELF_CONTAINED" = true ]; then
    BUILD_ARGS="$BUILD_ARGS --self-contained"
fi

# 构建 Windows 版本
if [ "$PLATFORM" = "All" ] || [ "$PLATFORM" = "Windows" ]; then
    echo "Building Windows version..."
    dotnet $BUILD_ARGS -r win-x64
    echo "Windows build completed"
fi

# 构建 macOS 版本
if [ "$PLATFORM" = "All" ] || [ "$PLATFORM" = "macOS" ]; then
    echo "Building macOS version..."
    dotnet $BUILD_ARGS -r osx-x64
    
    # 如果在 macOS 上构建，创建 .app 包
    if [[ "$OSTYPE" == "darwin"* ]]; then
        echo "Creating macOS .app bundle..."
        # TODO: 实现 .app 包创建逻辑
    fi
    
    echo "macOS build completed"
fi

# 构建 Linux 版本 (可选)
if [ "$PLATFORM" = "All" ] || [ "$PLATFORM" = "Linux" ]; then
    echo "Building Linux version..."
    dotnet $BUILD_ARGS -r linux-x64
    echo "Linux build completed"
fi

echo "Build process completed!"