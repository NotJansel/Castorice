#!/usr/bin/env bash
# Removes what build/macos/setup-ci-signing.sh put on the CI Mac. Safe to run when it did nothing.
set -uo pipefail

if [[ -n "${CASTORICE_SIGNING_KEYCHAIN:-}" ]]; then
  security delete-keychain "$CASTORICE_SIGNING_KEYCHAIN" 2>/dev/null || true
fi

if [[ -n "${CASTORICE_NOTARY_KEY_PATH:-}" ]]; then
  rm -f "$CASTORICE_NOTARY_KEY_PATH"
fi
