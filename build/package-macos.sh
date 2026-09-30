#!/usr/bin/env bash
# Builds Castorice.app, a self-contained macOS app bundle with its icon, and on a Mac also a
# disk image to install it from.
#
#   build/package-macos.sh              # for this Mac's processor
#   build/package-macos.sh osx-x64      # Intel
#   build/package-macos.sh osx-arm64    # Apple silicon
#
# The bundle lands in artifacts/macos/<rid>/Castorice.app and the disk image in
# artifacts/Castorice-<version>-macos-<arm64|x64>.dmg. CASTORICE_VERSION overrides the version
# from Directory.Build.props, as CI does for a tagged release.
#
# Signing. Without further settings the app is signed ad hoc: it runs on the Mac that built it,
# but a downloaded copy is stopped by Gatekeeper. For a build other Macs open without a warning:
#
#   CASTORICE_SIGN_IDENTITY   "Developer ID Application: Name (TEAMID)" from the keychain;
#                             signs with the hardened runtime and a secure timestamp
#
# and to notarise it with Apple, either an App Store Connect API key
#
#   CASTORICE_NOTARY_KEY_PATH, CASTORICE_NOTARY_KEY_ID, CASTORICE_NOTARY_ISSUER
#
# or an Apple ID with an app-specific password
#
#   CASTORICE_NOTARY_APPLE_ID, CASTORICE_NOTARY_PASSWORD, CASTORICE_NOTARY_TEAM_ID
#
# CASTORICE_SIGN_NO_TIMESTAMP=1 skips the timestamp, for test certificates Apple will not stamp.
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

version="${CASTORICE_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)}"
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
  -p:Version="$version" \
  -o "$contents/MacOS"

# macOS wants plain numbers in the bundle version, so a pre-release suffix such as -beta.1 is
# left out there; the file names keep it.
sed "s/@VERSION@/${version%%-*}/g" "$root/build/macos/Info.plist" > "$contents/Info.plist"
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

identity="${CASTORICE_SIGN_IDENTITY:-}"
entitlements="$root/build/macos/Castorice.entitlements"
timestamp=(--timestamp)
if [[ "${CASTORICE_SIGN_NO_TIMESTAMP:-}" == 1 ]]; then
  timestamp=(--timestamp=none)
fi

notary=()
if [[ -n "${CASTORICE_NOTARY_KEY_PATH:-}" && -n "${CASTORICE_NOTARY_KEY_ID:-}" && -n "${CASTORICE_NOTARY_ISSUER:-}" ]]; then
  notary=(--key "$CASTORICE_NOTARY_KEY_PATH" --key-id "$CASTORICE_NOTARY_KEY_ID" --issuer "$CASTORICE_NOTARY_ISSUER")
elif [[ -n "${CASTORICE_NOTARY_APPLE_ID:-}" && -n "${CASTORICE_NOTARY_PASSWORD:-}" && -n "${CASTORICE_NOTARY_TEAM_ID:-}" ]]; then
  notary=(--apple-id "$CASTORICE_NOTARY_APPLE_ID" --password "$CASTORICE_NOTARY_PASSWORD" --team-id "$CASTORICE_NOTARY_TEAM_ID")
fi

if [[ -z "$identity" && ${#notary[@]} -gt 0 ]]; then
  echo "Notarisation settings found but no CASTORICE_SIGN_IDENTITY; Apple only notarises Developer ID builds, so skipping it." >&2
  notary=()
fi

sign() {
  codesign --force "${timestamp[@]}" --options runtime --sign "$identity" "$@"
}

# Sends a file to Apple and waits for the verdict. notarytool exits 0 even for a rejected
# submission, so the status is read from its answer, and Apple's log is printed on failure.
notarise() {
  local file="$1" result id
  echo "Notarising $(basename "$file") — this usually takes a few minutes"
  result="$(xcrun notarytool submit "$file" "${notary[@]}" --wait --timeout 30m --output-format json)" || {
    echo "$result" >&2
    return 1
  }
  if ! grep -Eq '"status" *: *"Accepted"' <<<"$result"; then
    echo "Apple did not accept $(basename "$file"): $result" >&2
    id="$(sed -nE 's/.*"id" *: *"([^"]+)".*/\1/p' <<<"$result" | head -n 1)"
    [[ -n "$id" ]] && xcrun notarytool log "$id" "${notary[@]}" >&2 || true
    return 1
  fi
}

if ! command -v codesign >/dev/null; then
  echo "codesign not found; sign the bundle on a Mac before running it there:"
  echo "  codesign --force --deep --sign - \"$app\""
elif [[ -z "$identity" ]]; then
  # Apple silicon refuses to run unsigned code, and adding Info.plist and the icon changes the
  # bundle, so it is signed again ad hoc. That is enough to run it on the Mac that built it.
  codesign --force --deep --sign - "$app"
else
  echo "Signing with $identity"
  # Inside out: every file next to the executable (native libraries and managed assemblies
  # alike), then the executable with the entitlements the .NET runtime needs, then the bundle.
  find "$contents/MacOS" -type f ! -path "$contents/MacOS/Castorice" -print0 |
    while IFS= read -r -d '' file; do
      sign "$file" >/dev/null
    done
  sign --entitlements "$entitlements" "$contents/MacOS/Castorice"
  sign --entitlements "$entitlements" "$app"
  codesign --verify --deep --strict --verbose=2 "$app"

  if [[ ${#notary[@]} -gt 0 ]]; then
    # Notarising the app itself lets its ticket be stapled to it, so it opens without a network
    # check even once it has been copied out of the disk image.
    zip="$(mktemp -d)/Castorice.zip"
    ditto -c -k --keepParent "$app" "$zip"
    notarise "$zip"
    rm -rf "$(dirname "$zip")"
    xcrun stapler staple "$app"
  fi
fi

# The disk image: the app next to a shortcut to Applications, the usual drag-to-install window.
arch="${rid#osx-}"
dmg="$root/artifacts/Castorice-$version-macos-$arch.dmg"
if command -v hdiutil >/dev/null; then
  staging="$(mktemp -d)"
  cp -R "$app" "$staging/"
  ln -s /Applications "$staging/Applications"
  rm -f "$dmg"
  # hdiutil now and then fails with "Resource busy" on build machines; a retry gets past it.
  for attempt in 1 2 3; do
    if hdiutil create -volname "Castorice" -srcfolder "$staging" -fs HFS+ -format UDZO -ov "$dmg" >/dev/null; then
      break
    fi
    if [[ "$attempt" == 3 ]]; then
      echo "hdiutil could not create the disk image." >&2
      exit 1
    fi
    sleep 5
  done
  rm -rf "$staging"

  if [[ -n "$identity" ]]; then
    codesign --force "${timestamp[@]}" --sign "$identity" "$dmg"
    if [[ ${#notary[@]} -gt 0 ]]; then
      notarise "$dmg"
      xcrun stapler staple "$dmg"
      spctl --assess --type open --context context:primary-signature --verbose=2 "$dmg" || true
    fi
  fi

  echo "Disk image: $dmg"
else
  echo "hdiutil not found (not on a Mac?); skipping the disk image."
fi

echo
echo "Done: $app"
echo "Drag it into Applications, or run: open \"$app\""
