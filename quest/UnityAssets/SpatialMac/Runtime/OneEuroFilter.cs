using System;

namespace SpatialMac {
    // One Euro filter (Casiez, Roussel, Vogel; CHI 2012): a low-pass whose cutoff rises with speed.
    // Holding still => heavy smoothing (no hand jitter); moving fast => almost no lag.
    // Pure math with no Unity dependency so it can be unit-tested (quest/mathtests).
    public sealed class OneEuroFilter {
        public double MinCutoff, Beta, DerivativeCutoff;
        double prev, prevRaw, prevDelta; bool hasPrev;
        public OneEuroFilter(double minCutoff = 1.5, double beta = 1.0, double derivativeCutoff = 1.0) {
            MinCutoff = minCutoff; Beta = beta; DerivativeCutoff = derivativeCutoff;
        }
        public void Reset() { hasPrev = false; }
        static double Alpha(double cutoff, double dt) {
            double tau = 1.0 / (2.0 * Math.PI * cutoff);
            return 1.0 / (1.0 + tau / dt);
        }
        public double Filter(double x, double dt) {
            if (dt <= 1e-6) return hasPrev ? prev : x;
            if (!hasPrev) { hasPrev = true; prev = prevRaw = x; prevDelta = 0; return x; }
            double rawDelta = (x - prevRaw) / dt;
            prevRaw = x;
            prevDelta += Alpha(DerivativeCutoff, dt) * (rawDelta - prevDelta);
            double cutoff = MinCutoff + Beta * Math.Abs(prevDelta);
            prev += Alpha(cutoff, dt) * (x - prev);
            return prev;
        }
    }
}
