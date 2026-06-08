#!/usr/bin/env bash
# Builds the unsigned LaunchFast.app for osx-arm64 and zips it.
# Usage (from anywhere): build/macos/make-app.sh <version>
set -euo pipefail

VERSION="${1:?usage: make-app.sh <version>}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_NAME="LaunchFast"
EXE="LaunchFast.App"
RID="osx-arm64"

PUBLISH_DIR="$ROOT/artifacts/publish"
APP_DIR="$ROOT/artifacts/$APP_NAME.app"
ZIP="$ROOT/$APP_NAME-$VERSION-$RID.zip"

rm -rf "$PUBLISH_DIR" "$APP_DIR" "$ZIP"

dotnet publish "$ROOT/src/LaunchFast.App/LaunchFast.App.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" \
  -o "$PUBLISH_DIR"

mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"
sed "s/__VERSION__/$VERSION/g" "$ROOT/build/macos/Info.plist" > "$APP_DIR/Contents/Info.plist"

if [ -f "$ROOT/build/macos/$APP_NAME.icns" ]; then
  cp "$ROOT/build/macos/$APP_NAME.icns" "$APP_DIR/Contents/Resources/$APP_NAME.icns"
fi

test -f "$APP_DIR/Contents/MacOS/$EXE" \
  || { echo "error: apphost '$EXE' not found in publish output — did AssemblyName change?" >&2; exit 1; }
chmod +x "$APP_DIR/Contents/MacOS/$EXE"

# Ad-hoc sign the WHOLE bundle (--deep seals the apphost + all the self-contained .NET
# dylibs into one valid signature). arm64 requires a signature to run, and a valid
# bundle seal turns the Gatekeeper "damaged" error on a quarantined download into the
# normal bypassable "unidentified developer" prompt. NOT Developer-ID/notarized — see
# the readme for the right-click→Open / quarantine-removal step.
codesign --force --deep --sign - "$APP_DIR"
codesign --verify --strict "$APP_DIR"

ditto -c -k --keepParent "$APP_DIR" "$ZIP"
echo "Built $ZIP"
