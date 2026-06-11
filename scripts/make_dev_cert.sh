#!/bin/bash
# One-time setup: creates a self-signed "scrcap-dev" code-signing certificate
# in your login keychain and trusts it for code signing.
#
# Why: TCC (Screen Recording / Accessibility) identifies apps by their code
# signature. Ad-hoc signatures change on every build, so macOS treats each
# rebuild as a brand-new app and you have to re-grant permission. A stable
# identity + stable bundle ID means you grant once and rebuilds keep it.
#
# Usage: scripts/make_dev_cert.sh        (asks for sudo once, to trust the cert)
set -euo pipefail

NAME="scrcap-dev"

if security find-identity -v -p codesigning 2>/dev/null | grep -q "$NAME"; then
    echo "✓ '$NAME' identity already exists — nothing to do."
    exit 0
fi

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

echo "▸ generating self-signed code-signing certificate '$NAME'…"
cat > "$TMP/ext.cnf" <<'EOF'
[req]
distinguished_name = dn
[dn]
[ext]
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
basicConstraints = critical,CA:false
EOF

openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout "$TMP/key.pem" -out "$TMP/cert.pem" \
    -subj "/CN=$NAME" \
    -config "$TMP/ext.cnf" -extensions ext 2>/dev/null

openssl pkcs12 -export -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
    -out "$TMP/$NAME.p12" -passout pass:scrcap -name "$NAME"

echo "▸ importing into login keychain…"
security import "$TMP/$NAME.p12" -k "$HOME/Library/Keychains/login.keychain-db" \
    -P scrcap -T /usr/bin/codesign

echo "▸ trusting for code signing (sudo needed once)…"
sudo security add-trusted-cert -d -r trustRoot -p codeSign \
    -k /Library/Keychains/System.keychain "$TMP/cert.pem"

if security find-identity -v -p codesigning | grep -q "$NAME"; then
    echo "✓ '$NAME' is ready. scripts/make_app.sh will pick it up automatically."
    echo "  Grant Screen Recording to dist/scrcap.app once — rebuilds keep it."
else
    echo "✗ identity not visible to codesign — open Keychain Access and check" >&2
    echo "  that '$NAME' exists in the login keychain with its private key." >&2
    exit 1
fi
