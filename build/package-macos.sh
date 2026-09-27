#!/usr/bin/env bash
# Builds Castorice.app: a self-contained macOS app bundle with its icon, ready to drag into
# Applications.
#
#   build/package-macos.sh              # for this Mac's processor
#   build/package-macos.sh osx-x64      # Intel
#   build/package-macos.sh osx-arm64    # Apple silicon
#
# The result lands in artifacts/macos/<rid>/Castorice.app.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"

rid="${1:-}"
if [[ -z "$rid" ]]; then
  case "$(uname -m)" in
    x86_64) rid="osx-x64" ;;
    *) rid="osx-arm64" ;;
  esac
fi

if [[ "$rid" != osx-* ]]; then
  echo "Expected a macOS runtime id such as osx-arm64 or osx-x64, got '$rid'." >&2
  exit 1
fi

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)"
version="${version:-0.0.0}"

out="$root/artifacts/macos/$rid"
app="$out/Castorice.app"
contents="$app/Contents"

echo "Building Castorice $version for $rid"
rm -rf "$app"
mkdir -p "$contents/MacOS" "$contents/Resources"

# Self-contained, so the Mac it runs on does not need .NET installed.
dotnet publish "$root/src/Castorice.Desktop/Castorice.Desktop.csproj" \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -p:UseAppHost=true \
  -o "$contents/MacOS"

sed "s/@VERSION@/$version/g" "$root/build/macos/Info.plist" > "$contents/Info.plist"
printf 'APPL????' > "$contents/PkgInfo"

# The icon is built from the same artwork as the Dock icon, so replacing
# src/Castorice.Desktop/Assets/castorice-dock.png is all it takes to change it. Off a Mac, where
# sips and iconutil do not exist, the prebuilt build/macos/Castorice.icns is used instead.
icon_source="$root/src/Castorice.Desktop/Assets/castorice-dock.png"
if command -v iconutil >/dev/null && command -v sips >/dev/null; then
  iconset="$(mktemp -d)/Castorice.iconset"
  mkdir -p "$iconset"
  for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$icon_source" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$icon_source" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset" -o "$contents/Resources/Castorice.icns"
  rm -rf "$(dirname "$iconset")"
else
  echo "iconutil not found (not on a Mac?); using the prebuilt build/macos/Castorice.icns."
  cp "$root/build/macos/Castorice.icns" "$contents/Resources/Castorice.icns"
fi

# Apple silicon refuses to run unsigned code, and adding Info.plist and the icon changes the
# bundle, so it is signed again ad hoc. That is enough to run it on the Mac that built it.
if command -v codesign >/dev/null; then
  codesign --force --deep --sign - "$app"
else
  echo "codesign not found; sign the bundle on a Mac before running it there:"
  echo "  codesign --force --deep --sign - \"$app\""
fi

echo
echo "Done: $app"
echo "Drag it into Applications, or run: open \"$app\""
