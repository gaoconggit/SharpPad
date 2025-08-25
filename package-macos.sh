#!/bin/bash

# SharpPad macOS 一键打包脚本
# 使用方法: ./package-macos.sh

set -e  # 遇到错误立即退出

# 配置变量
APP_NAME="SharpPad"
BUNDLE_ID="com.sharppad.desktop"
VERSION="1.0.0"
PROJECT_PATH="SharpPad.Desktop/SharpPad.Desktop.csproj"

# 颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 打印带颜色的消息
print_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

print_step() {
    echo -e "${BLUE}📦 $1${NC}"
}

# 检查依赖
check_dependencies() {
    print_step "检查依赖..."
    
    if ! command -v dotnet &> /dev/null; then
        print_error ".NET SDK 未安装或未在PATH中"
        exit 1
    fi
    
    if ! command -v sips &> /dev/null; then
        print_warning "sips 命令未找到，将跳过图标转换"
    fi
    
    if ! command -v iconutil &> /dev/null; then
        print_warning "iconutil 命令未找到，将跳过图标转换"
    fi
    
    print_success "依赖检查完成"
}

# 清理之前的构建
clean_build() {
    print_step "清理之前的构建..."
    
    rm -rf "SharpPad.Desktop/bin" "SharpPad.Desktop/obj" 2>/dev/null || true
    rm -rf "SharpPad.app" 2>/dev/null || true
    rm -rf "publish-temp" 2>/dev/null || true
    rm -rf "icon.iconset" 2>/dev/null || true
    
    print_success "清理完成"
}

# 检测架构
detect_architecture() {
    ARCH=$(uname -m)
    if [ "$ARCH" = "arm64" ]; then
        RID="osx-arm64"
        print_info "检测到 Apple Silicon (ARM64)"
    else
        RID="osx-x64"
        print_info "检测到 Intel (x64)"
    fi
}

# 构建和发布应用
build_app() {
    print_step "构建应用程序 ($RID)..."
    
    dotnet publish "$PROJECT_PATH" \
        --configuration Release \
        --runtime "$RID" \
        --self-contained false \
        --output "publish-temp" \
        --verbosity quiet
    
    if [ $? -eq 0 ]; then
        print_success "应用构建完成"
    else
        print_error "应用构建失败"
        exit 1
    fi
}

# 创建应用包结构
create_app_structure() {
    print_step "创建应用包结构..."
    
    mkdir -p "SharpPad.app/Contents/MacOS"
    mkdir -p "SharpPad.app/Contents/Resources"
    
    print_success "应用包结构创建完成"
}

# 复制应用文件
copy_app_files() {
    print_step "复制应用文件..."
    
    cp -r publish-temp/* "SharpPad.app/Contents/MacOS/"
    
    print_success "应用文件复制完成"
}

# 创建Info.plist
create_info_plist() {
    print_step "创建Info.plist..."
    
    cat > "SharpPad.app/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>$APP_NAME</string>
	<key>CFBundleDisplayName</key>
	<string>$APP_NAME</string>
	<key>CFBundleIdentifier</key>
	<string>$BUNDLE_ID</string>
	<key>CFBundleVersion</key>
	<string>$VERSION</string>
	<key>CFBundleShortVersionString</key>
	<string>$VERSION</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleSignature</key>
	<string>SHPD</string>
	<key>CFBundleExecutable</key>
	<string>SharpPad.Desktop</string>
	<key>CFBundleIconFile</key>
	<string>SharpPad.icns</string>
	<key>LSMinimumSystemVersion</key>
	<string>10.15</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>NSPrincipalClass</key>
	<string>NSApplication</string>
	<key>NSHumanReadableCopyright</key>
	<string>© 2024 SharpPad. All rights reserved.</string>
	<key>CFBundleDocumentTypes</key>
	<array>
		<dict>
			<key>CFBundleTypeName</key>
			<string>C# Source File</string>
			<key>CFBundleTypeExtensions</key>
			<array>
				<string>cs</string>
			</array>
			<key>CFBundleTypeRole</key>
			<string>Editor</string>
			<key>LSHandlerRank</key>
			<string>Alternate</string>
		</dict>
	</array>
	<key>NSAppTransportSecurity</key>
	<dict>
		<key>NSAllowsLocalNetworking</key>
		<true/>
		<key>NSExceptionDomains</key>
		<dict>
			<key>localhost</key>
			<dict>
				<key>NSExceptionAllowsInsecureHTTPLoads</key>
				<true/>
			</dict>
		</dict>
	</dict>
</dict>
</plist>
EOF
    
    print_success "Info.plist 创建完成"
}

# 创建启动器脚本
create_launcher() {
    print_step "创建启动器脚本..."
    
    cat > "SharpPad.app/Contents/MacOS/SharpPad.Desktop" << 'EOF'
#!/bin/bash
cd "$(dirname "$0")"
exec dotnet SharpPad.Desktop.dll "$@"
EOF
    
    chmod +x "SharpPad.app/Contents/MacOS/SharpPad.Desktop"
    
    print_success "启动器脚本创建完成"
}

# 创建应用图标
create_app_icon() {
    print_step "创建应用图标..."
    
    ICNS_SOURCE="SharpPad.Desktop/Assets/favicon.icns"
    ICO_SOURCE="SharpPad.Desktop/Assets/favicon.ico"
    
    # 优先使用已有的 .icns 文件
    if [ -f "$ICNS_SOURCE" ]; then
        cp "$ICNS_SOURCE" "SharpPad.app/Contents/Resources/SharpPad.icns"
        print_success "应用图标复制完成 (使用现有 .icns 文件)"
        return
    fi
    
    # 如果没有 .icns 文件，则从 .ico 文件创建
    if [ -f "$ICO_SOURCE" ] && command -v sips &> /dev/null && command -v iconutil &> /dev/null; then
        # 创建图标集目录
        mkdir -p "icon.iconset"
        
        # 转换各种尺寸的图标
        sips -z 16 16 "$ICO_SOURCE" --out "icon.iconset/icon_16x16.png" 2>/dev/null || true
        sips -z 32 32 "$ICO_SOURCE" --out "icon.iconset/icon_16x16@2x.png" 2>/dev/null || true
        sips -z 32 32 "$ICO_SOURCE" --out "icon.iconset/icon_32x32.png" 2>/dev/null || true
        sips -z 64 64 "$ICO_SOURCE" --out "icon.iconset/icon_32x32@2x.png" 2>/dev/null || true
        sips -z 128 128 "$ICO_SOURCE" --out "icon.iconset/icon_128x128.png" 2>/dev/null || true
        sips -z 256 256 "$ICO_SOURCE" --out "icon.iconset/icon_128x128@2x.png" 2>/dev/null || true
        sips -z 256 256 "$ICO_SOURCE" --out "icon.iconset/icon_256x256.png" 2>/dev/null || true
        sips -z 512 512 "$ICO_SOURCE" --out "icon.iconset/icon_256x256@2x.png" 2>/dev/null || true
        sips -z 512 512 "$ICO_SOURCE" --out "icon.iconset/icon_512x512.png" 2>/dev/null || true
        sips -z 1024 1024 "$ICO_SOURCE" --out "icon.iconset/icon_512x512@2x.png" 2>/dev/null || true
        
        # 创建icns文件
        if iconutil -c icns "icon.iconset" -o "SharpPad.app/Contents/Resources/SharpPad.icns" 2>/dev/null; then
            print_success "应用图标创建完成 (从 .ico 文件转换)"
        else
            print_warning "图标转换失败，将使用默认图标"
        fi
        
        # 清理临时文件
        rm -rf "icon.iconset"
    else
        print_warning "跳过图标创建（图标文件不存在或缺少工具）"
    fi
}

# 代码签名
sign_app() {
    print_step "代码签名..."
    
    # 检查是否有开发者证书
    if security find-identity -v -p codesigning | grep -q "Developer ID Application" 2>/dev/null; then
        SIGNING_IDENTITY=$(security find-identity -v -p codesigning | grep "Developer ID Application" | head -n 1 | sed 's/.*) \(.*\)$/\1/')
        print_info "找到开发者证书: $SIGNING_IDENTITY"
        
        # 使用开发者证书签名
        if codesign --sign "$SIGNING_IDENTITY" --force --deep --options runtime "SharpPad.app" 2>/dev/null; then
            print_success "使用开发者证书签名完成"
            
            # 检查是否需要公证
            if command -v xcrun &> /dev/null && [ -n "$NOTARIZATION_PROFILE" ]; then
                print_info "开始公证应用..."
                if xcrun notarytool submit "SharpPad.app" --keychain-profile "$NOTARIZATION_PROFILE" --wait 2>/dev/null; then
                    xcrun stapler staple "SharpPad.app"
                    print_success "公证完成"
                else
                    print_warning "公证失败，但应用仍可在本机使用"
                fi
            else
                print_info "跳过公证（需要配置公证配置文件）"
            fi
        else
            print_warning "开发者证书签名失败，将使用临时签名"
            codesign --sign - --force --deep "SharpPad.app"
            print_success "临时签名完成"
        fi
    else
        print_info "未找到开发者证书，使用临时签名"
        if codesign --sign - --force --deep "SharpPad.app" 2>/dev/null; then
            print_success "临时签名完成"
        else
            print_error "签名失败"
            exit 1
        fi
    fi
}

# 设置最终权限
set_permissions() {
    print_step "设置权限..."
    
    # 清除隔离属性和其他扩展属性
    xattr -dr com.apple.quarantine "SharpPad.app" 2>/dev/null || true
    xattr -cr "SharpPad.app" 2>/dev/null || true
    
    # 确保启动脚本有执行权限
    chmod +x "SharpPad.app/Contents/MacOS/SharpPad.Desktop"
    
    print_success "权限设置完成"
}

# 清理临时文件
cleanup() {
    print_step "清理临时文件..."
    
    rm -rf "publish-temp" 2>/dev/null || true
    rm -rf "icon.iconset" 2>/dev/null || true
    
    print_success "清理完成"
}

# 验证应用包
verify_app() {
    print_step "验证应用包..."
    
    if [ -f "SharpPad.app/Contents/Info.plist" ] && [ -f "SharpPad.app/Contents/MacOS/SharpPad.Desktop" ]; then
        print_success "应用包验证通过"
    else
        print_error "应用包验证失败"
        exit 1
    fi
}

# 显示完成信息
show_completion_info() {
    echo ""
    echo "🎉 ${GREEN}SharpPad.app 打包完成！${NC}"
    echo ""
    echo "📍 位置: $(pwd)/SharpPad.app"
    echo "💾 大小: $(du -sh SharpPad.app | cut -f1)"
    echo ""
    echo "🚀 使用方法:"
    echo "   运行应用:     open SharpPad.app"
    echo "   双击启动:     支持双击直接启动"
    echo "   安装到应用:   cp -r SharpPad.app /Applications/"
    echo "   创建DMG:      hdiutil create -volname 'SharpPad' -srcfolder SharpPad.app -ov -format UDZO SharpPad.dmg"
    echo ""
    echo "📝 签名说明:"
    echo "   • 已自动进行代码签名，支持双击启动"
    echo "   • 如需分发给其他用户，建议使用Developer ID证书签名"
    echo "   • 公证配置: export NOTARIZATION_PROFILE=your_profile_name"
    echo ""
}

# 主函数
main() {
    echo "🔧 ${BLUE}SharpPad macOS 应用打包器${NC}"
    echo ""
    
    # 检查是否在正确的目录
    if [ ! -f "$PROJECT_PATH" ]; then
        print_error "未找到项目文件: $PROJECT_PATH"
        print_info "请在SharpPad项目根目录运行此脚本"
        exit 1
    fi
    
    # 执行所有步骤
    check_dependencies
    clean_build
    detect_architecture
    build_app
    create_app_structure
    copy_app_files
    create_info_plist
    create_launcher
    create_app_icon
    set_permissions
    sign_app
    cleanup
    verify_app
    show_completion_info
}

# 运行主函数
main "$@"