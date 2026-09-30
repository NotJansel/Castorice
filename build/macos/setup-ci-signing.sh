#!/usr/bin/env bash
# Prepares a CI Mac for signing and notarising from repository secrets, and hands the result to
# build/package-macos.sh through $GITHUB_ENV. Without a certificate it does nothing, and the app
# is signed ad hoc as before.
#
#   MACOS_CERTIFICATE_P12        the "Developer ID Application" certificate with its private key,
#                                exported as .p12 and base64-encoded
#   MACOS_CERTIFICATE_PASSWORD   the .p12's password
#   MACOS_SIGNING_IDENTITY       optional; picked from the certificate when left out
#   MACOS_NOTARY_KEY             optional; the App Store Connect API key (.p8), as is or base64
#
# The temporary keychain is removed again by build/macos/cleanup-ci-signing.sh.
set -euo pipefail

: "${RUNNER_TEMP:?RUNNER_TEMP is not set; this script is meant for GitHub Actions}"
: "${GITHUB_ENV:?GITHUB_ENV is not set; this script is meant for GitHub Actions}"

if [[ -z "${MACOS_CERTIFICATE_P12:-}" ]]; then
  echo "No signing certificate configured (MACOS_CERTIFICATE_P12); the app will be signed ad hoc."
  exit 0
fi

keychain="$RUNNER_TEMP/castorice-signing.keychain-db"
keychain_password="$(openssl rand -base64 24)"

security create-keychain -p "$keychain_password" "$keychain"
# Stays unlocked for six hours, longer than any build.
security set-keychain-settings -lut 21600 "$keychain"
security unlock-keychain -p "$keychain_password" "$keychain"

certificate="$RUNNER_TEMP/castorice-signing.p12"
printf '%s' "$MACOS_CERTIFICATE_P12" | base64 --decode > "$certificate"
security import "$certificate" -k "$keychain" -P "${MACOS_CERTIFICATE_PASSWORD:-}" \
  -T /usr/bin/codesign -T /usr/bin/security
rm -f "$certificate"

# A certificate exported on its own lacks Apple's intermediate, without which the identity does
# not count as valid. Harmless when it is already there.
intermediate="$RUNNER_TEMP/DeveloperIDG2CA.cer"
if curl -fsSL -o "$intermediate" https://www.apple.com/certificateauthority/DeveloperIDG2CA.cer; then
  security import "$intermediate" -k "$keychain" >/dev/null 2>&1 || true
  rm -f "$intermediate"
else
  echo "Could not download Apple's Developer ID intermediate certificate; continuing without it."
fi

# Lets codesign use the key without a password prompt nobody could answer.
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$keychain_password" "$keychain" >/dev/null

# Adds the keychain to the search list, keeping the ones already there.
existing=()
while IFS= read -r line; do
  line="${line//\"/}"
  line="${line#"${line%%[![:space:]]*}"}"
  [[ -n "$line" ]] && existing+=("$line")
done < <(security list-keychains -d user)
security list-keychains -d user -s "$keychain" "${existing[@]}"

identities="$(security find-identity -v -p codesigning "$keychain")"
identity="${MACOS_SIGNING_IDENTITY:-}"
if [[ -z "$identity" ]]; then
  # A Developer ID if there is one, otherwise whatever valid identity the certificate holds.
  identity="$(sed -nE 's/^ *[0-9]+\) [0-9A-F]+ "(Developer ID Application:.*)"$/\1/p' <<<"$identities" | head -n 1)"
  if [[ -z "$identity" ]]; then
    identity="$(sed -nE 's/^ *[0-9]+\) [0-9A-F]+ "(.*)"$/\1/p' <<<"$identities" | head -n 1)"
  fi
fi

if [[ -z "$identity" ]]; then
  echo "::error::The certificate in MACOS_CERTIFICATE_P12 holds no valid code signing identity."
  echo "$identities"
  exit 1
fi

echo "Signing identity: $identity"
{
  echo "CASTORICE_SIGN_IDENTITY=$identity"
  echo "CASTORICE_SIGNING_KEYCHAIN=$keychain"
} >> "$GITHUB_ENV"

if [[ -n "${MACOS_NOTARY_KEY:-}" ]]; then
  key="$RUNNER_TEMP/castorice-notary-key.p8"
  if [[ "$MACOS_NOTARY_KEY" == *"BEGIN PRIVATE KEY"* ]]; then
    printf '%s\n' "$MACOS_NOTARY_KEY" > "$key"
  else
    printf '%s' "$MACOS_NOTARY_KEY" | base64 --decode > "$key"
  fi
  chmod 600 "$key"
  echo "CASTORICE_NOTARY_KEY_PATH=$key" >> "$GITHUB_ENV"
fi
