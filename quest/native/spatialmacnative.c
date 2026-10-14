// Tiny JNI helper for SpatialMac: returns the memory address of a java.nio direct ByteBuffer so Unity can copy a
// decoded video plane straight out of MediaCodec's buffer. Unity's public AndroidJNI API has no such call, and
// copying through a managed byte[] would allocate megabytes of garbage per frame at 60 fps.
//
// No libc is used (just the JNI function table), so this builds without an NDK sysroot:
//   scripts/build-native.sh
#include <jni.h>
#include <stdint.h>

JNIEXPORT jlong JNICALL Java_org_spatialmac_SpatialMacDecoder_nativeAddress(JNIEnv *env, jclass clazz, jobject buffer) {
    (void)clazz;
    if (buffer == 0) return 0;
    void *p = (*env)->GetDirectBufferAddress(env, buffer);
    return (jlong)(intptr_t)p;
}

JNIEXPORT jlong JNICALL Java_org_spatialmac_SpatialMacDecoder_nativeCapacity(JNIEnv *env, jclass clazz, jobject buffer) {
    (void)clazz;
    if (buffer == 0) return 0;
    return (jlong)(*env)->GetDirectBufferCapacity(env, buffer);
}
