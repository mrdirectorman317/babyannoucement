#!/bin/bash
set -u
printf '\nSpatialMac read-only environment report\n'
uname -srm
for TOOL in swift xcodebuild python3 openssl adb; do
 if command -v "$TOOL" >/dev/null; then printf '%s: %s\n' "$TOOL" "$(command -v "$TOOL")"; else printf '%s: NOT FOUND\n' "$TOOL"; fi
done
command -v swift >/dev/null && swift --version
command -v adb >/dev/null && adb devices
if [[ "$(uname -s)" == Darwin ]]; then sw_vers; xcode-select -p 2>/dev/null || true; fi
printf '\nThis does not test Unity compatibility, Screen Recording, Accessibility, or headset permissions.\n'
