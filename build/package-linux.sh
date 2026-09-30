#!/usr/bin/env bash
# Builds Castorice for Linux: an AppImage that runs on most distributions without installing
# anything, and a plain .tar.gz of the same self-contained build.
#
#   build/package-linux.sh              # x64
#   build/package-linux.sh linux-arm64  # ARM64
#
# Results land in artifacts/Castorice-<version>-linux-<arch>.AppImage and .tar.gz.
# CASTORICE_VERSION overrides the version from Directory.Build.props, as CI does for a tagged
# release. appimagetool is downloaded into artifacts/tools on first use; set APPIMAGETOOL to use
# one already installed.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"

rid="${1:-linux-x64}"
case "$rid" in
  linux-x64) arch="x86_64" ;;
  linux-arm64) arch="aarch64" ;;
  *) echo "Expected linux-x64 or linux-arm64, got '$rid'." >&2; exit 1 ;;
esac

version="${CASTORICE_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)}"
version="${version:-0.0.0}"

artifacts="$root/artifacts"
appdir="$artifacts/linux/$rid/Castorice.AppDir"
publish="$appdir/usr/lib/castorice"

echo "Building Castorice $version for $rid"
rm -rf "$appdir"
mkdir -p "$publish" "$appdir/usr/bin" \
  "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/256x256/apps"

# Self-contained, so nothing but the usual desktop libraries (X11, fontconfig, ICU) is needed.
dotnet publish "$root/src/Castorice.Desktop/Castorice.Desktop.csproj" \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -p:UseAppHost=true \
  -p:Version="$version" \
  -o "$publish"

ln -s ../lib/castorice/Castorice "$appdir/usr/bin/Castorice"

# The desktop entry and icon sit both at the AppDir root, where appimagetool looks, and in the
# usual share/ locations, where desktop integration tools look.
install -m 755 "$root/build/linux/AppRun" "$appdir/AppRun"
install -m 644 "$root/build/linux/castorice.desktop" "$appdir/castorice.desktop"
install -m 644 "$root/build/linux/castorice.desktop" "$appdir/usr/share/applications/castorice.desktop"
install -m 644 "$root/src/Castorice.Desktop/Assets/castorice.png" "$appdir/castorice.png"
install -m 644 "$root/src/Castorice.Desktop/Assets/castorice.png" \
  "$appdir/usr/share/icons/hicolor/256x256/apps/castorice.png"
ln -s castorice.png "$appdir/.DirIcon"

# The plain archive: the published build in one folder, for unpacking anywhere.
tarball="$artifacts/Castorice-$version-linux-$arch.tar.gz"
tar -czf "$tarball" -C "$appdir/usr/lib" --transform "s:^castorice:Castorice-$version:" castorice
echo "Archive: $tarball"

tool="${APPIMAGETOOL:-}"
if [[ -z "$tool" ]]; then
  host_arch="$(uname -m)"
  tool="$artifacts/tools/appimagetool-$host_arch.AppImage"
  if [[ ! -x "$tool" ]]; then
    mkdir -p "$(dirname "$tool")"
    echo "Downloading appimagetool for $host_arch"
    curl -fsSL -o "$tool" \
      "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$host_arch.AppImage"
    chmod +x "$tool"
  fi
fi

appimage="$artifacts/Castorice-$version-linux-$arch.AppImage"
# Extract-and-run: build machines and containers often have no FUSE to mount the tool with.
APPIMAGE_EXTRACT_AND_RUN=1 ARCH="$arch" "$tool" --no-appstream "$appdir" "$appimage"

echo
echo "Done: $appimage"
echo "Make it executable and start it: chmod +x \"$appimage\" && \"$appimage\""
