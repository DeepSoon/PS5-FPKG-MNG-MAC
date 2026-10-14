#!/bin/bash
# PS5 FPKG Builder — macOS nativo (SwiftUI, 1.0.0-NG, Made by Leite)
# Monta "PS5 FPKG Builder.app" com binário Swift + backend .NET self-contained.
set -euo pipefail
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAC="$SRC/mac"
APP="$MAC/PS5 FPKG Builder.app"
RES="$APP/Contents/Resources"
MACOS="$APP/Contents/MacOS"

echo "== backend .NET =="
dotnet publish "$MAC/FpkgMac/FpkgMac.csproj" -c Release -r osx-arm64 --self-contained true -o "$MAC/publish-arm64" 2>&1 | tail -n 2
cp -f "$SRC/LibProsperoPkg.dll" "$SRC/BCnEncoder.dll" "$SRC/CommunityToolkit.HighPerformance.dll" "$SRC/VERSION.txt" "$MAC/publish-arm64/"
rm -rf "$MAC/publish-arm64/exfat"
mkdir -p "$MAC/publish-arm64/exfat"
cp -R "$MAC/exfat/"* "$MAC/publish-arm64/exfat/"
find "$MAC/publish-arm64/exfat" -name __pycache__ -type d -exec rm -rf {} + 2>/dev/null || true

echo "== UI Swift =="
swiftc -parse-as-library -o "$MAC/FpkgMacUI" \
  "$MAC/swift/I18n.swift" "$MAC/swift/Runner.swift" "$MAC/swift/Theme.swift" "$MAC/swift/App.swift" \
  "$MAC/swift/PagesA.swift" "$MAC/swift/PagesB.swift" "$MAC/swift/PagesC.swift" -O

echo "== bundle .app =="
rm -rf "$APP"
mkdir -p "$RES/FpkgMac" "$MACOS"
cp -f "$MAC/FpkgMacUI" "$MACOS/PS5 FPKG Builder"
chmod +x "$MACOS/PS5 FPKG Builder"
cp -R "$MAC/publish-arm64/"* "$RES/FpkgMac/"
chmod +x "$RES/FpkgMac/FpkgMac"
cp -R "$MAC/exfat" "$RES/exfat" 2>/dev/null || true
cp -f "$SRC/THIRD_PARTY_NOTICES.md" "$RES/" 2>/dev/null || true
if [ -f "$SRC/assets/zer0day-mark.png" ]; then cp -f "$SRC/assets/zer0day-mark.png" "$RES/icon.png"; fi

# icone .icns (reaproveita se já existir no build anterior)
if [ ! -f "$RES/AppIcon.icns" ]; then
  TMPICON=$(mktemp -d)/AppIcon.iconset
  mkdir -p "$TMPICON"
  for s in 16 32 64 128 256 512; do
    sips -z $s $s "$SRC/assets/zer0day-mark.png" --out "$TMPICON/icon_${s}x${s}.png" >/dev/null 2>&1 || break
  done
  iconutil -c icns "$TMPICON" -o "$RES/AppIcon.icns" >/dev/null 2>&1 || true
fi

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>PS5 FPKG Builder</string>
  <key>CFBundleDisplayName</key><string>PS5 FPKG Builder</string>
  <key>CFBundleIdentifier</key><string>com.zer0day.ps5-fpkg-builder.mac</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundleShortVersionString</key><string>1.0.0-NG</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>PS5 FPKG Builder</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
</dict>
</plist>
PLIST

cat > "$RES/README-MAC.txt" <<'TXT'
PS5 FPKG Builder 1.0.0-NG — macOS nativo (SwiftUI) · Made by Leite

Abrir: duplo-clique em "PS5 FPKG Builder.app".
Se o macOS bloquear: botão direito > Abrir > Abrir.

Interface 100% nativa (Swift, sem Python/Tkinter). Backend .NET
autocontido (arm64) + LibProsperoPkg + leitor exFAT embutido.
Requer macOS 14 ou superior. Não usa rede. Idiomas: PT-BR / EN.

Créditos originais: Zer0Day, Drakmor, svenGDK, EchoStretch/cragson,
kerrdec97, MkPFS. Tema anterior: Sun Valley (rdbende, MIT).
TXT

codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true
xattr -dr com.apple.quarantine "$APP" >/dev/null 2>&1 || true
echo "APP OK: $APP"
ls -lh "$MACOS/" | cat
