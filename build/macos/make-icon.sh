#!/usr/bin/env bash
# One-time: regenerate build/macos/LaunchFast.icns (commit the result).
# Requires ImageMagick:  brew install imagemagick
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
ICONSET="$WORK/icon.iconset"
mkdir -p "$ICONSET"

magick -size 1024x1024 xc:none \
  -fill '#1E8E64' -draw 'roundrectangle 96,96 928,928 200,200' \
  -fill white -gravity center -pointsize 440 -font Helvetica-Bold -annotate 0 'LF' \
  "$WORK/base.png"

gen() { sips -z "$2" "$2" "$WORK/base.png" --out "$ICONSET/$1" >/dev/null; }
gen icon_16x16.png 16
gen icon_16x16@2x.png 32
gen icon_32x32.png 32
gen icon_32x32@2x.png 64
gen icon_128x128.png 128
gen icon_128x128@2x.png 256
gen icon_256x256.png 256
gen icon_256x256@2x.png 512
gen icon_512x512.png 512
gen icon_512x512@2x.png 1024

iconutil -c icns "$ICONSET" -o "$ROOT/build/macos/LaunchFast.icns"
echo "Wrote build/macos/LaunchFast.icns"
