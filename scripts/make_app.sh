#!/bin/bash
# Builds scrcap in release mode and packages it as scrcap.app.
# Usage: scripts/make_app.sh [output-dir]   (default: ./dist)
#
# Signing: uses $CODESIGN_IDENTITY if set; otherwise auto-detects the
# "scrcap-dev" self-signed identity (create once with scripts/make_dev_cert.sh
# so TCC permissions survive rebuilds); otherwise falls back to ad-hoc.
set -euo pipefail

cd "$(dirname "$0")/.."
OUT="${1:-dist}"
APP="$OUT/scrcap.app"

echo "▸ building release (-Osize, dead-strip)…"
swift build -c release -Xswiftc -Osize -Xlinker -dead_strip 2>&1 | tail -1

echo "▸ assembling bundle…"
rm -rf "$APP"
# Minimal bundle: binary + Info.plist. No Resources, no PkgInfo — nothing
# that isn't required.
mkdir -p "$APP/Contents/MacOS"

cp .build/release/scrcap "$APP/Contents/MacOS/scrcap"
# Strip symbol table and debug info from the shipped binary.
strip -Sx "$APP/Contents/MacOS/scrcap" 2>/dev/null || true

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>          <string>scrcap</string>
    <key>CFBundleIdentifier</key>          <string>com.scrcap.app</string>
    <key>CFBundleName</key>                <string>scrcap</string>
    <key>CFBundleDisplayName</key>         <string>scrcap</string>
    <key>CFBundlePackageType</key>         <string>APPL</string>
    <key>CFBundleShortVersionString</key>  <string>1.0</string>
    <key>CFBundleVersion</key>             <string>1</string>
    <key>LSMinimumSystemVersion</key>      <string>14.0</string>
    <key>LSUIElement</key>                 <true/>
    <key>NSHighResolutionCapable</key>     <true/>
    <key>NSPrincipalClass</key>            <string>NSApplication</string>
</dict>
</plist>
PLIST

# A stable signing identity keeps TCC grants across rebuilds; an ad-hoc
# signature ("-") changes identity every build and resets permissions.
IDENTITY="${CODESIGN_IDENTITY:-}"
if [ -z "$IDENTITY" ] && security find-identity -v -p codesigning 2>/dev/null | grep -q "scrcap-dev"; then
    IDENTITY="scrcap-dev"
fi
if [ -n "$IDENTITY" ]; then
    echo "▸ signing with identity: $IDENTITY"
    codesign --force --sign "$IDENTITY" --identifier com.scrcap.app "$APP"
else
    echo "▸ signing ad-hoc (note: TCC permissions reset every rebuild —"
    echo "  run scripts/make_dev_cert.sh once to fix that)"
    codesign --force --sign - --identifier com.scrcap.app "$APP"
fi

echo "▸ checking budgets (plan §08)…"
scripts/check_budgets.sh "$APP"

echo "✓ $APP"
