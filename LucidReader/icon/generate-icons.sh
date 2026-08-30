#!/usr/bin/env bash
# Regenerates every mylo application icon raster from mylo-icon.svg.
#
# Usage: LucidReader/icon/generate-icons.sh
#
# Outputs, all committed so a build and the release workflow need neither
# rsvg-convert nor Pillow:
#
#   LucidReader/icon/mylo.icns        the macOS bundle icon (CFBundleIconFile)
#   LucidReader/icon/mylo.ico         the Windows ApplicationIcon
#   LucidReader/Assets/mylo.png       the Avalonia Window.Icon, 512 square
#
# The Assets copy is the only one that ships inside the single-file executable:
# Assets\** is an AvaloniaResource, and an .icns embedded there would be a
# hundred kilobytes nobody reads at runtime. That is why the icns and ico live
# in this folder instead.
#
# Needs rsvg-convert (brew install librsvg) and python3 with Pillow.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SVG="$HERE/mylo-icon.svg"
ASSETS="$(cd "$HERE/.." && pwd)/Assets"

for tool in rsvg-convert python3 iconutil; do
    command -v "$tool" >/dev/null 2>&1 || { echo "missing: $tool" >&2; exit 1; }
done
python3 -c "import PIL" 2>/dev/null || { echo "missing: python3 Pillow" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

render() { rsvg-convert -w "$1" -h "$1" "$SVG" -o "$2"; }

# The window and taskbar icon.
render 512 "$ASSETS/mylo.png"

# The macOS bundle icon. iconutil insists on this exact set of names.
ICONSET="$WORK/mylo.iconset"
mkdir -p "$ICONSET"
render 16   "$ICONSET/icon_16x16.png"
render 32   "$ICONSET/icon_16x16@2x.png"
render 32   "$ICONSET/icon_32x32.png"
render 64   "$ICONSET/icon_32x32@2x.png"
render 128  "$ICONSET/icon_128x128.png"
render 256  "$ICONSET/icon_128x128@2x.png"
render 256  "$ICONSET/icon_256x256.png"
render 512  "$ICONSET/icon_256x256@2x.png"
render 512  "$ICONSET/icon_512x512.png"
render 1024 "$ICONSET/icon_512x512@2x.png"
iconutil -c icns "$ICONSET" -o "$HERE/mylo.icns"

# The Windows icon. Pillow writes every listed size into one .ico.
for size in 16 24 32 48 64 128 256; do
    render "$size" "$WORK/ico-$size.png"
done
python3 - "$WORK" "$HERE/mylo.ico" <<'PY'
import sys
from PIL import Image

work, out = sys.argv[1], sys.argv[2]
sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [Image.open(f"{work}/ico-{s}.png").convert("RGBA") for s in sizes]
frames[-1].save(out, format="ICO", sizes=[(s, s) for s in sizes])
PY

echo "wrote $ASSETS/mylo.png, $HERE/mylo.icns, $HERE/mylo.ico"
