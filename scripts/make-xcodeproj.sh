#!/bin/bash
# Build the host as a real Xcode app (needed for Siri / Shortcuts App Intents discovery).
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'Requires macOS with Xcode.' >&2; exit 1; }
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
command -v xcodebuild >/dev/null || { echo 'Install Xcode (not just command-line tools).' >&2; exit 1; }
if ! command -v xcodegen >/dev/null; then
  command -v brew >/dev/null || { echo 'Install Homebrew, then: brew install xcodegen' >&2; exit 1; }
  brew install xcodegen
fi
cd "$ROOT/macOS"
xcodegen generate --spec project.yml
xcodebuild -project SpatialMac.xcodeproj -scheme SpatialMacHost -configuration Release \
  -derivedDataPath "$ROOT/Build/DerivedData" CODE_SIGN_IDENTITY=- build
APP="$ROOT/Build/DerivedData/Build/Products/Release/SpatialMacHost.app"
mkdir -p "$ROOT/Build"; rm -rf "$ROOT/Build/SpatialMac.app"; cp -R "$APP" "$ROOT/Build/SpatialMac.app"
echo "Built: $ROOT/Build/SpatialMac.app"
echo 'Open it once, then try: "Hey Siri, open my SpatialMac workspace" (Shortcuts app lists the actions too).'
