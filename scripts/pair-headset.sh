#!/bin/bash
set -euo pipefail
command -v adb >/dev/null || { echo 'Install Android platform-tools; adb is required.' >&2; exit 1; }
PACKAGE="${SPATIALMAC_PACKAGE:-org.spatialmac.quest}"
CONFIG="${SPATIALMAC_CONFIG:-$HOME/Library/Application Support/SpatialMac/client-config.json}"
[[ -f "$CONFIG" ]] || { echo 'Run setup-mac.sh first.' >&2; exit 1; }
if [[ -n "${ANDROID_SERIAL:-}" ]]; then
  SERIAL="$ANDROID_SERIAL"
else
  DEVICES="$(adb devices | awk 'NR>1 && $2=="device" {print $1}')"
  COUNT="$(printf '%s\n' "$DEVICES" | awk 'NF{n++} END{print n+0}')"
  [[ "$COUNT" == 1 ]] || { echo 'Connect exactly one authorized headset, or set ANDROID_SERIAL.' >&2; exit 1; }
  SERIAL="$DEVICES"
fi
ADB=(adb -s "$SERIAL")
"${ADB[@]}" shell pm path "$PACKAGE" | grep -q '^package:' || { echo 'Install and launch the Unity APK once before pairing.' >&2; exit 1; }
"${ADB[@]}" reverse tcp:9443 tcp:9443
DEST="/sdcard/Android/data/$PACKAGE/files"
"${ADB[@]}" shell mkdir -p "$DEST"
"${ADB[@]}" push "$CONFIG" "$DEST/client-config.json" >/dev/null
echo 'Pairing copied and USB tunnel established. Restart the Quest app.'
echo 'Keep this developer headset authorized only on computers you trust. Do not publish the configuration.'
