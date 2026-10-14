#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'The host app requires macOS and an Apple SDK.' >&2; exit 1; }
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
command -v swift >/dev/null || { echo 'Install Xcode command-line tools first.' >&2; exit 1; }
cd "$ROOT/macOS"
swift test
swift build -c release --product SpatialMacHost
BIN="$(swift build -c release --show-bin-path)/SpatialMacHost"
APP="$ROOT/Build/SpatialMacHost.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/SpatialMacHost"
cp "$ROOT/macOS/Resources/Info.plist" "$APP/Contents/Info.plist"
# Local developer build, NOT notarized distribution. Stable signing should be
# configured before routine upgrades to reduce TCC re-authorization surprises.
codesign --force --sign - "$APP"
echo "Built: $APP"
echo 'Open this app, grant Screen Recording, approve a source, then explicitly enable sharing.'
echo 'Grant Accessibility only when you are ready to arm remote input. Rebuilds may need reapproval.'
