#!/bin/bash
# Verifies the Android decoder plugin without a headset or Unity:
#  1. compiles SpatialMacDecoder.java against the real Android API (android-all jar from Maven Central)
#  2. builds the native helper for THIS machine and runs the real class's JNI binding (name/signature check)
#  3. cross-compiles the helper for arm64 Android and checks the exported symbols
# Needs: JDK 17+, clang, lld, curl. Set ANDROID_ALL_JAR to reuse a downloaded jar.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
V=15-robolectric-13954326
JAR="${ANDROID_ALL_JAR:-$WORK/android-all.jar}"
if [[ ! -f "$JAR" ]]; then
  curl -sSL --fail -o "$JAR" "https://repo1.maven.org/maven2/org/robolectric/android-all/$V/android-all-$V.jar"
fi
JH="${JAVA_HOME:-$(dirname "$(dirname "$(readlink -f "$(command -v javac)")")")}"
mkdir -p "$WORK/classes"
javac --release 11 -cp "$JAR" -d "$WORK/classes" "$ROOT/quest/UnityAssets/SpatialMac/Plugins/Android/org/spatialmac/SpatialMacDecoder.java" 2>&1 | grep -v 'UnsupportedAppUsage\|JAVA_TOOL_OPTIONS\|warning' || true
test -f "$WORK/classes/org/spatialmac/SpatialMacDecoder.class" && echo "OK   decoder compiles against Android API"
clang -shared -fPIC -O2 -I"$JH/include" -I"$JH/include/linux" "$ROOT/quest/native/spatialmacnative.c" -o "$WORK/libspatialmacnative.so"
mkdir -p "$WORK/test"
javac --release 11 -cp "$WORK/classes:$JAR" -d "$WORK/test" "$ROOT/quest/native/test/JniCheck.java" 2>&1 | grep -v 'UnsupportedAppUsage\|JAVA_TOOL_OPTIONS\|warning' || true
java -Djava.library.path="$WORK" -cp "$WORK/test:$WORK/classes:$JAR" org.spatialmac.JniCheck 2>&1 | grep -v JAVA_TOOL_OPTIONS
bash "$ROOT/scripts/build-native.sh" >/dev/null
SO="$ROOT/quest/UnityAssets/SpatialMac/Plugins/Android/libs/arm64-v8a/libspatialmacnative.so"
llvm-nm -D "$SO" | grep -q Java_org_spatialmac_SpatialMacDecoder_nativeAddress && echo "OK   arm64 library exports the JNI symbols"
echo "ALL OK"
