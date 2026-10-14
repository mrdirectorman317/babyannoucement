package org.spatialmac;

import java.nio.file.Files;
import java.nio.file.Paths;

/** Checks the codec-config splitter against the real H.264 and HEVC fixtures' first access units (keyframes). */
public class ParamSetCheck {
    static int firstUnitLength(byte[] d, boolean hevc) {
        // the first access unit ends where the second picture NAL's start code begins
        int pictures = 0;
        for (int i = 0; i + 4 < d.length; i++) {
            boolean sc4 = d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 0 && d[i + 3] == 1;
            boolean sc3 = d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1;
            if (!sc4 && !sc3) continue;
            int b = d[i + (sc4 ? 4 : 3)] & 0xFF;
            boolean picture = hevc ? ((b >> 1) & 0x3F) < 32 : ((b & 0x1F) == 1 || (b & 0x1F) == 5);
            if (picture && ++pictures == 2) return i;
        }
        return d.length;
    }
    static void check(String file, boolean hevc, int expectSets) throws Exception {
        byte[] d = Files.readAllBytes(Paths.get(file));
        int n = firstUnitLength(d, hevc);
        int prefix = SpatialMacDecoder.parameterSetPrefixLength(d, n, hevc);
        int b = d[prefix + (d[prefix + 2] == 1 ? 3 : 4)] & 0xFF;
        int type = hevc ? (b >> 1) & 0x3F : b & 0x1F;
        int sets = 0;
        for (int i = 0; i + 4 < prefix; i++) if (d[i] == 0 && d[i + 1] == 0 && (d[i + 2] == 1 || (d[i + 2] == 0 && d[i + 3] == 1))) sets++;
        System.out.println(file.substring(file.lastIndexOf('/') + 1) + ": prefix=" + prefix + " bytes, NALs before picture>=" + sets + ", picture NAL type=" + type);
        boolean ok = prefix > 0 && prefix < n && (hevc ? type == 19 || type == 20 : type == 5) && sets >= expectSets;
        if (!ok) { System.out.println("FAIL"); System.exit(1); }
    }
    public static void main(String[] a) throws Exception {
        check(a[0], false, 2);   // SPS + PPS (x264 may add an SEI)
        check(a[1], true, 3);    // VPS + SPS + PPS
        // degenerate inputs must not crash or loop
        if (SpatialMacDecoder.parameterSetPrefixLength(new byte[]{1, 2, 3}, 3, false) != 0) System.exit(1);
        if (SpatialMacDecoder.parameterSetPrefixLength(new byte[0], 0, true) != 0) System.exit(1);
        if (SpatialMacDecoder.parameterSetPrefixLength(new byte[]{0, 0, 0, 1, 0x67, 1, 2, 3}, 8, false) != 0) System.exit(1);   // sets but no picture
        System.out.println("PASS");
    }
}
