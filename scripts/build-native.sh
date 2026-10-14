#!/bin/bash
# Cross-compiles quest/native/spatialmacnative.c to an arm64 Android shared library (needs clang + lld + a JDK for jni.h;
# any recent LLVM works, no Android NDK sysroot required because the code uses no libc).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/quest/UnityAssets/SpatialMac/Plugins/Android/libs/arm64-v8a"
JNI_DIR="$(dirname "$(find "${JAVA_HOME:-/usr/lib/jvm}" -name jni.h 2>/dev/null | head -1)")"
[[ -f "$JNI_DIR/jni.h" ]] || { echo 'jni.h not found (install a JDK or set JAVA_HOME)' >&2; exit 1; }
mkdir -p "$OUT"
RES="$(clang --print-resource-dir)/include"
clang --target=aarch64-linux-android29 -shared -fPIC -O2 -nostdlib -nostdinc -ffreestanding -fuse-ld=lld \
  -Wl,-z,max-page-size=16384 -Wl,--build-id=none -Wl,-soname,libspatialmacnative.so \
  -I"$ROOT/quest/native/shim" -isystem "$RES" -I"$JNI_DIR" -I"$JNI_DIR/linux" \
  "$ROOT/quest/native/spatialmacnative.c" -o "$OUT/libspatialmacnative.so"
llvm-strip --strip-unneeded "$OUT/libspatialmacnative.so" 2>/dev/null || strip "$OUT/libspatialmacnative.so" || true
echo "Built $OUT/libspatialmacnative.so"
llvm-nm -D "$OUT/libspatialmacnative.so" | grep Java_org_spatialmac
