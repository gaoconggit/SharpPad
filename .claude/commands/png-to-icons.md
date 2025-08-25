---
description: Convert PNG file to ICO (Windows) and ICNS (macOS) formats
allowed_tools: [Bash, Write, TodoWrite]
---

Convert the PNG file at `$ARGUMENTS` to both ICO and ICNS formats suitable for Windows and macOS applications.

This command will:
1. Create a Windows ICO file with multiple icon sizes (16x16, 32x32, 48x48, 64x64, 128x128, 256x256)
2. Create a macOS ICNS file with all standard resolutions including Retina variants
3. Clean up temporary files

The converted files will be created in the same directory as the source PNG file.

Usage: `/png-to-icons path/to/your/file.png`

## Manual Steps (if command fails)

### 1. Create ICNS file (macOS format)

```bash
# Navigate to the directory containing your PNG file
cd "path/to/your/assets"

# Create iconset directory
mkdir -p favicon.iconset

# Generate all required icon sizes
sips -z 16 16 favicon.png --out favicon.iconset/icon_16x16.png
sips -z 32 32 favicon.png --out favicon.iconset/icon_16x16@2x.png
sips -z 32 32 favicon.png --out favicon.iconset/icon_32x32.png
sips -z 64 64 favicon.png --out favicon.iconset/icon_32x32@2x.png
sips -z 128 128 favicon.png --out favicon.iconset/icon_128x128.png
sips -z 256 256 favicon.png --out favicon.iconset/icon_128x128@2x.png
sips -z 256 256 favicon.png --out favicon.iconset/icon_256x256.png
sips -z 512 512 favicon.png --out favicon.iconset/icon_256x256@2x.png
sips -z 512 512 favicon.png --out favicon.iconset/icon_512x512.png
sips -z 1024 1024 favicon.png --out favicon.iconset/icon_512x512@2x.png

# Convert iconset to ICNS
iconutil -c icns favicon.iconset

# Clean up temporary directory
rm -rf favicon.iconset
```

### 2. Create ICO file (Windows format)

#### Method A: Using Python with PIL/Pillow (recommended)

```python
#!/usr/bin/env python3
from PIL import Image
import os

def create_ico(png_path, ico_path):
    img = Image.open(png_path)
    sizes = [16, 32, 48, 64, 128, 256]
    icons = []
    
    for size in sizes:
        resized = img.resize((size, size), Image.Resampling.LANCZOS)
        icons.append(resized)
    
    icons[0].save(ico_path, format='ICO', sizes=[(size, size) for size in sizes])
    print(f"Successfully created {ico_path}")

# Usage
create_ico("favicon.png", "favicon.ico")
```

**Prerequisites:**
```bash
pip3 install Pillow
```

#### Method B: Fallback (PNG format ICO)

```bash
# Simple copy as fallback (works on most systems)
cp favicon.png favicon.ico
```

### 3. Verification

Check that both files were created:
```bash
ls -la *.ic*
file favicon.icns
file favicon.ico
```

## Expected Results

- **favicon.icns**: Native macOS icon with multiple resolutions (16x16 to 1024x1024, including Retina variants)
- **favicon.ico**: Windows icon with multiple sizes (16x16 to 256x256)

## Common Issues

1. **sips ICO conversion fails**: Use Python method instead
2. **PIL/Pillow not available**: Install with `pip3 install Pillow` or use fallback method
3. **Permission errors**: Check write permissions in target directory
4. **Source PNG issues**: Verify PNG file integrity with `file filename.png`

## Dependencies

- **macOS**: `sips` (built-in), `iconutil` (built-in)
- **Python ICO**: `PIL/Pillow` package
- **Alternative**: ImageMagick (`brew install imagemagick`)

## Usage in Applications

### Avalonia Desktop App
Reference the icon files in your project:
- Windows: Use `favicon.ico`
- macOS: Use `favicon.icns`
- Cross-platform: Both files in Assets directory

### Configuration
Add to your `.csproj` or application manifest as needed for proper icon display in OS taskbars and file explorers.