#!/bin/bash

# macOS App Bundle Builder for SharpPad
# This script creates a proper .app bundle for macOS

set -e

PROJECT_ROOT="$(pwd)"
APP_NAME="SharpPad"
BUNDLE_ID="com.sharppad.desktop"
VERSION="1.0.0"

# Determine architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

echo "Building SharpPad.app for $ARCH ($RID)..."

# Clean and build
echo "Cleaning previous builds..."
rm -rf "SharpPad.Desktop/bin" "SharpPad.Desktop/obj"
rm -rf "SharpPad.app"

# Publish the application
echo "Publishing application..."
dotnet publish SharpPad.Desktop/SharpPad.Desktop.csproj \
    -c Release \
    -r $RID \
    --self-contained false \
    -o "publish-temp"

# Create app bundle structure
echo "Creating app bundle structure..."
mkdir -p "SharpPad.app/Contents/MacOS"
mkdir -p "SharpPad.app/Contents/Resources"

# Copy executable and dependencies
echo "Copying application files..."
cp -r "publish-temp/"* "SharpPad.app/Contents/MacOS/"

# Copy Info.plist
cp "SharpPad.Desktop/Info.plist" "SharpPad.app/Contents/"

# Create or copy icon (convert from ico to icns if needed)
if [ -f "SharpPad.Desktop/Assets/favicon.ico" ]; then
    echo "Converting icon..."
    # Create temporary iconset
    mkdir -p "icon.iconset"
    
    # Use sips to convert and resize (macOS built-in tool)
    sips -z 16 16 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_16x16.png" 2>/dev/null || echo "Warning: Could not create 16x16 icon"
    sips -z 32 32 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_16x16@2x.png" 2>/dev/null || echo "Warning: Could not create 16x16@2x icon"
    sips -z 32 32 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_32x32.png" 2>/dev/null || echo "Warning: Could not create 32x32 icon"
    sips -z 64 64 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_32x32@2x.png" 2>/dev/null || echo "Warning: Could not create 32x32@2x icon"
    sips -z 128 128 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_128x128.png" 2>/dev/null || echo "Warning: Could not create 128x128 icon"
    sips -z 256 256 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_128x128@2x.png" 2>/dev/null || echo "Warning: Could not create 128x128@2x icon"
    sips -z 256 256 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_256x256.png" 2>/dev/null || echo "Warning: Could not create 256x256 icon"
    sips -z 512 512 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_256x256@2x.png" 2>/dev/null || echo "Warning: Could not create 256x256@2x icon"
    sips -z 512 512 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_512x512.png" 2>/dev/null || echo "Warning: Could not create 512x512 icon"
    sips -z 1024 1024 "SharpPad.Desktop/Assets/favicon.ico" --out "icon.iconset/icon_512x512@2x.png" 2>/dev/null || echo "Warning: Could not create 512x512@2x icon"
    
    # Create icns file
    iconutil -c icns "icon.iconset" -o "SharpPad.app/Contents/Resources/SharpPad.icns"
    
    # Clean up
    rm -rf "icon.iconset"
    
    echo "Icon created successfully"
else
    echo "Warning: Icon file not found, using default icon"
fi

# Create launcher script for SharpPad.Desktop
cat > "SharpPad.app/Contents/MacOS/SharpPad.Desktop" << 'EOF'
#!/bin/bash
cd "$(dirname "$0")"
exec dotnet SharpPad.Desktop.dll "$@"
EOF

# Make executable
chmod +x "SharpPad.app/Contents/MacOS/SharpPad.Desktop"

# Clean up temporary files
rm -rf "publish-temp"

# Set extended attributes for app bundle
xattr -cr "SharpPad.app"

echo "✅ SharpPad.app created successfully!"
echo "📦 Location: $PROJECT_ROOT/SharpPad.app"
echo ""
echo "To run the app:"
echo "  open SharpPad.app"
echo ""
echo "To install the app:"
echo "  cp -r SharpPad.app /Applications/"
echo ""
echo "To create a DMG (optional):"
echo "  hdiutil create -volname 'SharpPad' -srcfolder SharpPad.app -ov -format UDZO SharpPad.dmg"