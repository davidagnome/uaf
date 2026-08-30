#!/usr/bin/env bash
#
# release.sh — builds the port for macOS, Linux and Windows and packages each into a distributable
# artifact.
#
#   tools/release.sh
#
# Produces, under out/release/:
#   - Windows: a .zip of the win-x64 self-contained build.
#   - macOS:   a .app bundle (per architecture) plus a .zip of it.
#   - Linux:   a .tar.gz, and an AppImage when appimagetool is on PATH.
#
# Notarization (macOS) and MSIX signing (Windows) are deliberate stubs: they need Apple and
# Microsoft signing credentials and will not run without them. Set NOTARIZE=1 (with
# APPLE_ID / APPLE_APP_PASSWORD / APPLE_TEAM_ID) to attempt macOS notarization after the bundle is
# built; MSIX is not produced at all, because it cannot be signed without a Windows certificate.
#
# The version stamps come from VERSION, else `git describe`; the .app bundle uses it in its
# Info.plist, so set VERSION=5.29.0 (or similar) for a release build.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/out/release"
VERSION="${VERSION:-$(git -C "$ROOT" describe --tags --always 2>/dev/null || echo 0.0.0)}"

# The runtime identifiers this release covers.
RIDS=(win-x64 osx-x64 osx-arm64 linux-x64)

info() { printf '\033[1;36m== %s\033[0m\n' "$*"; }

info "publishing $VERSION for: ${RIDS[*]}"
rm -rf "$OUT"
mkdir -p "$OUT"
"$ROOT/tools/publish.sh" "${RIDS[@]}"

package_windows() {
  local src="$1" rid="$2" zip_path="$OUT/UAFcore-${VERSION}-${rid}.zip"
  info "packaging Windows ($rid)"
  (cd "$ROOT/out/publish" && zip -qr "$zip_path" "$rid")
  echo "  $zip_path"
}

package_macos() {
  local src="$1" rid="$2" app="$OUT/DungeonCraft-${VERSION}-${rid}.app"
  info "packaging macOS ($rid)"
  rm -rf "$app"
  mkdir -p "$app/Contents/MacOS"

  # The self-contained publish output goes in MacOS/ beside the apphost, which resolves its DLLs and
  # the bundled runtime relative to its own directory -- so the whole folder moves as one.
  cp -R "$src/." "$app/Contents/MacOS/"

  cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>UAFcoreApp</string>
    <key>CFBundleIdentifier</key>
    <string>org.uaf.UAFcore</string>
    <key>CFBundleName</key>
    <string>Dungeon Craft</string>
    <key>CFBundleDisplayName</key>
    <string>Dungeon Craft</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

  (cd "$OUT" && zip -qr "${app}.zip" "$(basename "$app")")
  echo "  ${app}.zip"
}

package_linux() {
  local src="$1" rid="$2" name="UAFcore-${VERSION}-${rid}"
  info "packaging Linux ($rid)"

  tar -C "$ROOT/out/publish" -czf "$OUT/$name.tar.gz" "$rid"
  echo "  $OUT/$name.tar.gz"

  if command -v appimagetool >/dev/null 2>&1; then
    local appdir="$OUT/$name.AppDir"
    mkdir -p "$appdir/usr/bin"
    cp -R "$src/." "$appdir/usr/bin/"
    cat > "$appdir/AppRun" <<'EOF'
#!/bin/sh
exec "$(dirname "$0")/usr/bin/UAFcoreApp" "$@"
EOF
    chmod +x "$appdir/AppRun"
    cat > "$appdir/uafcore.desktop" <<EOF
[Desktop Entry]
Name=Dungeon Craft
Exec=UAFcoreApp
Type=Application
Categories=Game;
EOF
    appimagetool "$appdir" "$OUT/$name.AppImage"
    rm -rf "$appdir"
    echo "  $OUT/$name.AppImage"
  else
    echo "  (skipping AppImage: appimagetool is not on PATH)"
  fi
}

for rid in "${RIDS[@]}"; do
  src="$ROOT/out/publish/$rid"
  case "$rid" in
    win-*)   package_windows "$src" "$rid" ;;
    osx-*)   package_macos   "$src" "$rid" ;;
    linux-*) package_linux   "$src" "$rid" ;;
  esac
done

if [ "${NOTARIZE:-0}" = "1" ]; then
  info "notarizing macOS bundles"
  for credential in APPLE_ID APPLE_APP_PASSWORD APPLE_TEAM_ID; do
    if [ -z "${!credential:-}" ]; then
      echo "::error::NOTARIZE=1 but $credential is not set" >&2
      exit 1
    fi
  done

  for app in "$OUT"/*.app; do
    codesign --force --deep --sign "Developer ID Application" "$app"
    ditto -c -k --keepParent "$app" "$app.notarize.zip"
    xcrun notarytool submit "$app.notarize.zip" \
      --apple-id "$APPLE_ID" --password "$APPLE_APP_PASSWORD" --team-id "$APPLE_TEAM_ID" --wait
    xcrun stapler staple "$app"
    rm -f "$app.notarize.zip"
  done
else
  info "not notarizing (set NOTARIZE=1 with APPLE_ID / APPLE_APP_PASSWORD / APPLE_TEAM_ID)"
fi

info "release complete"
find "$OUT" -maxdepth 1 -type f -o -maxdepth 1 -type d | sort | sed "s#$OUT/##"
