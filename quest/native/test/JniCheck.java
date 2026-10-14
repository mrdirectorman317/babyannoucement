package org.spatialmac;
import java.lang.reflect.Method;
import java.nio.ByteBuffer;
public class JniCheck {
    public static void main(String[] a) throws Exception {
        // Loading SpatialMacDecoder runs its static initializer, which must find libspatialmacnative via java.library.path.
        Class<?> c = Class.forName("org.spatialmac.SpatialMacDecoder");
        Method addr = c.getDeclaredMethod("nativeAddress", ByteBuffer.class), cap = c.getDeclaredMethod("nativeCapacity", ByteBuffer.class);
        addr.setAccessible(true); cap.setAccessible(true);
        ByteBuffer direct = ByteBuffer.allocateDirect(4096);
        long p = (Long) addr.invoke(null, direct), n = (Long) cap.invoke(null, direct);
        long heapAddr = (Long) addr.invoke(null, ByteBuffer.allocate(16));
        long nullAddr = (Long) addr.invoke(null, new Object[]{null});
        System.out.println("direct address nonzero=" + (p != 0) + " capacity=" + n + " heap->" + heapAddr + " null->" + nullAddr);
        if (p == 0 || n != 4096 || heapAddr != 0 || nullAddr != 0) { System.out.println("FAIL"); System.exit(1); }
        System.out.println("PASS");
    }
}
