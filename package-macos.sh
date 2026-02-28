#!/bin/bash

set -e

APP_NAME="SharpPad"
BUNDLE_ID="com.sharppad.desktop"
VERSION="1.0.0"
PROJECT_PATH="SharpPad.Desktop/SharpPad.Desktop.csproj"
BACKGROUND_IMG="dmg-background.png"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

print_info() { echo -e "${BLUE}ℹ️  $1${NC}"; }
print_success() { echo -e "${GREEN}✅ $1${NC}"; }
print_warning() { echo -e "${YELLOW}⚠️  $1${NC}"; }
print_error() { echo -e "${RED}❌ $1${NC}"; }
print_step() { echo -e "${BLUE}📦 $1${NC}"; }

check_dependencies() {
    print_step "检查依赖"

    command -v dotnet >/dev/null || { print_error ".NET 未安装"; exit 1; }

    if ! command -v create-dmg >/dev/null; then
        print_error "需要安装 create-dmg: brew install create-dmg"
        exit 1
    fi

    print_success "依赖检查完成"
}

clean_build() {
    print_step "清理旧文件"
    rm -rf publish-temp
    rm -rf "$APP_NAME.app"
    rm -f "$APP_NAME-$VERSION.dmg"
    print_success "清理完成"
}

detect_arch() {
    ARCH=$(uname -m)
    if [ "$ARCH" = "arm64" ]; then
        RID="osx-arm64"
    else
        RID="osx-x64"
    fi
    print_info "架构: $RID"
}

build_app() {
    print_step "发布应用"

    dotnet publish "$PROJECT_PATH" \
        -c Release \
        -r "$RID" \
        --self-contained true \
        -o publish-temp \
        --verbosity quiet

    print_success "发布完成"
}

create_app_bundle() {
    print_step "创建 .app 结构"

    mkdir -p "$APP_NAME.app/Contents/MacOS"
    mkdir -p "$APP_NAME.app/Contents/Resources"

    cp -R publish-temp/* "$APP_NAME.app/Contents/MacOS/"

    rm -rf "$APP_NAME.app/Contents/MacOS/runtimes/win"* || true
    rm -rf "$APP_NAME.app/Contents/MacOS/runtimes/linux"* || true

    chmod +x "$APP_NAME.app/Contents/MacOS/SharpPad.Desktop"

    print_success ".app 创建完成"
}

create_info_plist() {
cat > "$APP_NAME.app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
"http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
<key>CFBundleName</key><string>$APP_NAME</string>
<key>CFBundleDisplayName</key><string>$APP_NAME</string>
<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
<key>CFBundleVersion</key><string>$VERSION</string>
<key>CFBundleShortVersionString</key><string>$VERSION</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleExecutable</key><string>SharpPad.Desktop</string>
<key>CFBundleIconFile</key><string>$APP_NAME.icns</string>
<key>LSMinimumSystemVersion</key><string>10.15</string>
<key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF
}

copy_icon() {
    ICON_SRC="SharpPad.Desktop/Assets/favicon.icns"
    if [ -f "$ICON_SRC" ]; then
        cp "$ICON_SRC" "$APP_NAME.app/Contents/Resources/$APP_NAME.icns"
        print_success "图标复制完成"
    else
        print_warning "未找到 icns 图标"
    fi
}

sign_app() {
    print_step "签名应用"

    if security find-identity -v -p codesigning | grep -q "Developer ID Application"; then
        SIGN_ID=$(security find-identity -v -p codesigning | \
                  grep "Developer ID Application" | head -n1 | \
                  sed 's/.*) \(.*\)$/\1/')
        codesign --force --deep --options runtime \
                 --sign "$SIGN_ID" "$APP_NAME.app"
        print_success "Developer ID 签名完成"
    else
        codesign --force --deep -s - "$APP_NAME.app"
        print_warning "使用 ad-hoc 签名"
    fi

    xattr -cr "$APP_NAME.app"
}

create_dmg() {
    print_step "创建 DMG"

    DMG_NAME="$APP_NAME-$VERSION.dmg"
    ICON_PATH="$APP_NAME.app/Contents/Resources/$APP_NAME.icns"

    if [ ! -f "$BACKGROUND_IMG" ]; then
        print_error "缺少背景图: $BACKGROUND_IMG"
        exit 1
    fi

    if [ ! -f "$ICON_PATH" ]; then
        print_warning "未找到 DMG 卷图标，将不设置 --volicon"
        VOLICON_OPTION=""
    else
        VOLICON_OPTION="--volicon $ICON_PATH"
        print_info "使用卷图标: $ICON_PATH"
    fi

    create-dmg \
      --volname "$APP_NAME" \
      $VOLICON_OPTION \
      --background "$BACKGROUND_IMG" \
      --window-size 600 400 \
      --icon-size 120 \
      --icon "$APP_NAME.app" 150 200 \
      --app-drop-link 450 200 \
      --hide-extension "$APP_NAME.app" \
      --no-internet-enable \
      "$DMG_NAME" \
      "$APP_NAME.app"

    print_success "DMG 创建完成: $DMG_NAME"
}

cleanup() {
    rm -rf publish-temp
}

main() {
    echo "🔧 SharpPad macOS 打包开始"
    check_dependencies
    clean_build
    detect_arch
    build_app
    create_app_bundle
    create_info_plist
    copy_icon
    sign_app
    create_dmg
    cleanup

    echo ""
    echo "🎉 打包完成"
    echo "APP: $(pwd)/$APP_NAME.app"
    echo "DMG: $(pwd)/$APP_NAME-$VERSION.dmg"
}

main "$@"