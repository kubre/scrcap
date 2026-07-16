#!/bin/bash
# Builds scrcap in release mode and packages it as scrcap.app. Pass --prod to
# also create release zip and DMG artifacts for manual publishing.
# Usage: scripts/make_app.sh [--prod] [output-dir]   (default: ./dist)
#
# Development builds use $CODESIGN_IDENTITY or the local "scrcap-dev"
# identity. Production builds require Developer ID + notarization unless
# SCRCAP_ALLOW_UNNOTARIZED=1 is set explicitly.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd -P)"
PROD=0
OUT="dist"

while [ "$#" -gt 0 ]; do
    case "$1" in
        --prod)
            PROD=1
            ;;
        -h|--help)
            echo "Usage: scripts/make_app.sh [--prod] [output-dir]"
            exit 0
            ;;
        --*)
            echo "✗ unknown option: $1" >&2
            exit 1
            ;;
        *)
            OUT="$1"
            ;;
    esac
    shift
done

VERSION="${SCRCAP_VERSION:-}"
if [ -z "$VERSION" ]; then
    TAG="$(git describe --tags --exact-match 2>/dev/null || true)"
    case "$TAG" in
        v[0-9]*|[0-9]*)
            VERSION="${TAG#v}"
            ;;
    esac
fi

if [ -z "$VERSION" ]; then
    if [ "$PROD" -eq 1 ]; then
        echo "✗ production builds need SCRCAP_VERSION or an exact version tag (for example v1.2.3)" >&2
        exit 1
    fi
    VERSION="0.0.0"
fi
VERSION="${VERSION#v}"

if [[ ! "$VERSION" =~ ^[0-9]+([.][0-9]+){0,2}$ ]]; then
    echo "✗ invalid SCRCAP_VERSION: $VERSION" >&2
    echo "  Use a numeric version such as 1.2.3." >&2
    exit 1
fi

BUILD_NUMBER="${SCRCAP_BUILD:-$(git rev-list --count HEAD 2>/dev/null || echo 1)}"
if [[ ! "$BUILD_NUMBER" =~ ^[0-9]+$ ]]; then
    echo "✗ invalid SCRCAP_BUILD: $BUILD_NUMBER" >&2
    echo "  Use an integer build number." >&2
    exit 1
fi

if [[ "$OUT" != "dist" && ! "$OUT" =~ ^dist-[A-Za-z0-9._-]+$ ]]; then
    echo "✗ output must be dist or a dist-* directory name" >&2
    exit 1
fi
OUT="$ROOT/$OUT"
APP="$OUT/scrcap.app"
ZIP="$OUT/scrcap-macos.zip"
DMG="$OUT/scrcap-macos.dmg"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

IDENTITY="${CODESIGN_IDENTITY:-}"
ALLOW_UNNOTARIZED="${SCRCAP_ALLOW_UNNOTARIZED:-}"
NOTARY_PROFILE="${SCRCAP_NOTARY_PROFILE:-}"
if [ "$PROD" -eq 1 ] && [ "$ALLOW_UNNOTARIZED" != "1" ]; then
    if [[ "$IDENTITY" != *"Developer ID Application"* ]]; then
        echo "✗ production builds require CODESIGN_IDENTITY='Developer ID Application: …'" >&2
        echo "  For an intentional unnotarized build: SCRCAP_ALLOW_UNNOTARIZED=1" >&2
        exit 1
    fi
    if [ -z "$NOTARY_PROFILE" ]; then
        echo "✗ production builds require SCRCAP_NOTARY_PROFILE" >&2
        echo "  Create one with: xcrun notarytool store-credentials <profile>" >&2
        exit 1
    fi
fi

echo "▸ building release (-Osize, whole-module, dead-strip)…"
swift build -c release \
    -Xswiftc -Osize \
    -Xswiftc -whole-module-optimization \
    -Xlinker -dead_strip 2>&1 | tail -1

echo "▸ assembling bundle (version $VERSION, build $BUILD_NUMBER)…"
rm -rf "$OUT"
# Minimal bundle: binary + Info.plist + icons. No PkgInfo or SwiftPM build
# metadata.
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp .build/release/scrcap "$APP/Contents/MacOS/scrcap"
cp Sources/scrcap/Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
cp Sources/scrcap/Resources/MenuBarIconTemplate.png "$APP/Contents/Resources/MenuBarIconTemplate.png"
# Strip symbol/debug metadata from the shipped binary. This does not remove app
# features; it only drops linker/debug information that is not executed.
strip -ru "$APP/Contents/MacOS/scrcap" 2>/dev/null || strip -Sx "$APP/Contents/MacOS/scrcap" 2>/dev/null || true

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>          <string>scrcap</string>
    <key>CFBundleIdentifier</key>          <string>com.scrcap.app</string>
    <key>CFBundleName</key>                <string>scrcap</string>
    <key>CFBundleDisplayName</key>         <string>scrcap</string>
    <key>CFBundlePackageType</key>         <string>APPL</string>
    <key>CFBundleIconFile</key>            <string>AppIcon</string>
    <key>CFBundleShortVersionString</key>  <string>$VERSION</string>
    <key>CFBundleVersion</key>             <string>$BUILD_NUMBER</string>
    <key>LSMinimumSystemVersion</key>      <string>14.0</string>
    <key>LSUIElement</key>                 <true/>
    <key>NSHighResolutionCapable</key>     <true/>
    <key>NSPrincipalClass</key>            <string>NSApplication</string>
</dict>
</plist>
PLIST

# A stable signing identity keeps TCC grants across rebuilds; an ad-hoc
# signature ("-") changes identity every build and resets permissions.
if [ -z "$IDENTITY" ] && security find-identity -v -p codesigning 2>/dev/null | grep -q "scrcap-dev"; then
    IDENTITY="scrcap-dev"
fi
if [ -n "$IDENTITY" ]; then
    echo "▸ signing with identity: $IDENTITY"
    SIGN_ARGS=(--force --sign "$IDENTITY" --identifier com.scrcap.app)
    if [ "$PROD" -eq 1 ] && [ "$ALLOW_UNNOTARIZED" != "1" ]; then
        SIGN_ARGS+=(--options runtime --timestamp)
    fi
    codesign "${SIGN_ARGS[@]}" "$APP"
elif [ "${SCRCAP_ALLOW_ADHOC:-}" = "1" ]; then
    echo "▸ signing ad-hoc (TCC permissions will reset on rebuild)"
    codesign --force --sign - --identifier com.scrcap.app "$APP"
else
    echo "✗ no stable code-signing identity found." >&2
    echo "  Run scripts/make_dev_cert.sh once, or set CODESIGN_IDENTITY." >&2
    echo "  For a throwaway build only: SCRCAP_ALLOW_ADHOC=1 scripts/make_app.sh" >&2
    exit 1
fi
codesign --verify --deep --strict --verbose=2 "$APP"

echo "▸ checking release budgets…"
scripts/check_budgets.sh "$APP"

if [ "$PROD" -ne 1 ]; then
    echo "✓ $APP"
    echo "  skipped release zip and DMG (pass --prod to create them)"
    exit 0
fi

echo "▸ creating compressed release zip…"
create_zip() {
    rm -f "$ZIP"
    (
        cd "$OUT"
        COPYFILE_DISABLE=1 zip -qry -9 "$(basename "$ZIP")" "$(basename "$APP")" \
            -x "*.DS_Store" "*/.DS_Store" "__MACOSX/*" "*/._*"
    )
}
create_zip

if [ "$ALLOW_UNNOTARIZED" = "1" ]; then
    echo "⚠ production artifact is intentionally unnotarized"
else
    echo "▸ notarizing app…"
    xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$APP"
    create_zip
fi

if unzip -Z1 "$ZIP" | grep -E '(^|/)(__MACOSX|\.DS_Store|\.build|[^/]*\._|.*\.dSYM(/|$)|.*\.swiftmodule$|.*\.swiftdoc$|.*\.swiftinterface$|.*\.o$|.*\.a$|.*\.pcm$)' >/dev/null; then
    echo "✗ release zip FAIL: $ZIP contains development files" >&2
    unzip -Z1 "$ZIP" | grep -E '(^|/)(__MACOSX|\.DS_Store|\.build|[^/]*\._|.*\.dSYM(/|$)|.*\.swiftmodule$|.*\.swiftdoc$|.*\.swiftinterface$|.*\.o$|.*\.a$|.*\.pcm$)' >&2
    exit 1
fi

echo "▸ creating compressed DMG…"
DMG_ROOT="$TMP_DIR/dmg-root"
mkdir -p "$DMG_ROOT"
ditto "$APP" "$DMG_ROOT/scrcap.app"
ln -s /Applications "$DMG_ROOT/Applications"
rm -f "$DMG"
hdiutil create -quiet \
    -volname "scrcap" \
    -srcfolder "$DMG_ROOT" \
    -format UDZO \
    -imagekey zlib-level=9 \
    "$DMG"

if [ "$ALLOW_UNNOTARIZED" != "1" ]; then
    echo "▸ notarizing DMG…"
    xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$DMG"
    xcrun stapler validate "$APP"
    xcrun stapler validate "$DMG"
    spctl --assess --type execute --verbose=2 "$APP"
fi

echo "✓ $APP"
echo "✓ $ZIP ($(du -sh "$ZIP" | cut -f1))"
echo "✓ $DMG ($(du -sh "$DMG" | cut -f1))"
