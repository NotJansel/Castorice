#!/usr/bin/env bash
# Makes a throwaway, self-signed code signing certificate on a CI Mac and trusts it, so the
# Developer ID signing path can be exercised without Apple's certificate. The result goes into
# $GITHUB_ENV in the same form as the real secrets, for build/macos/setup-ci-signing.sh.
# Never use this for anything that is handed out: nothing but this machine trusts it.
set -euo pipefail

: "${RUNNER_TEMP:?}" "${GITHUB_ENV:?}"

dir="$RUNNER_TEMP/castorice-test-certificate"
mkdir -p "$dir"

cat > "$dir/openssl.cnf" <<'CNF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = Castorice CI Test Signing
[ext]
basicConstraints = critical, CA:false
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
CNF

# The system's LibreSSL writes a .p12 that the macOS keychain imports without complaint.
/usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 2 \
  -config "$dir/openssl.cnf" -keyout "$dir/key.pem" -out "$dir/cert.pem"
/usr/bin/openssl pkcs12 -export -inkey "$dir/key.pem" -in "$dir/cert.pem" \
  -out "$dir/cert.p12" -passout pass:castorice-test

# Trusted for code signing on this machine only, so codesign treats it as a valid identity.
sudo security add-trusted-cert -d -r trustRoot -p codeSign -k /Library/Keychains/System.keychain "$dir/cert.pem"

{
  echo "MACOS_CERTIFICATE_P12=$(base64 -i "$dir/cert.p12" | tr -d '\n')"
  echo "MACOS_CERTIFICATE_PASSWORD=castorice-test"
} >> "$GITHUB_ENV"

rm -rf "$dir"
