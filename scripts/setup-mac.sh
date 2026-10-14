#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'Run this setup on the Mac that will share its screen.' >&2; exit 1; }
for tool in openssl python3; do command -v "$tool" >/dev/null || { echo "Missing: $tool" >&2; exit 1; }; done
BASE="$HOME/Library/Application Support/SpatialMac"
umask 077
mkdir -p "$BASE"; chmod 700 "$BASE"
if [[ -e "$BASE/host-settings.json" && "${1:-}" != --rotate ]]; then
  echo 'Pairing already exists. Leave it intact, or explicitly run with --rotate to invalidate the previous pairing.'; exit 0
fi
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
openssl rand -hex 32 > "$TMP/password"
cat > "$TMP/openssl.cnf" <<'CONF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = spatialmac.local
[ext]
subjectAltName = DNS:spatialmac.local,DNS:localhost,IP:127.0.0.1
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature,keyEncipherment
extendedKeyUsage = serverAuth
CONF
openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 365 \
  -keyout "$TMP/key.pem" -out "$TMP/cert.pem" -config "$TMP/openssl.cnf"
# Legacy algorithms here concern the password-protected PKCS#12 CONTAINER,
# for Apple import compatibility. Network connections still require TLS >= 1.2.
openssl pkcs12 -export -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
  -out "$TMP/identity.p12" -passout "file:$TMP/password" \
  -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1
openssl x509 -in "$TMP/cert.pem" -outform DER -out "$TMP/cert.der"
python3 - "$TMP" "$BASE" <<'PYCODE'
from pathlib import Path
import hashlib,json,secrets,sys,uuid,shutil,os
src,dst=map(Path,sys.argv[1:]);token=secrets.token_hex(32)
host=dict(token=token,p12Password=(src/'password').read_text().strip(),bindLAN=False,port=9443,
          whisperExecutable=None,whisperModel=None)
client=dict(host='127.0.0.1',port=9443,certificateSHA256=hashlib.sha256((src/'cert.der').read_bytes()).hexdigest(),
            token=token,clientID=str(uuid.uuid4()),autoConnect=True)
for name,data in [('host-settings.json',host),('client-config.json',client)]:
    (dst/name).write_text(json.dumps(data,indent=2)+'\n');os.chmod(dst/name,0o600)
for name in ['identity.p12','cert.pem']:
    shutil.copy2(src/name,dst/name);os.chmod(dst/name,0o600)
print('Created private host settings and one client pairing. No tokens printed.')
print('Certificate expires in 365 days; explicit rotation and headset re-pairing are required.')
PYCODE
