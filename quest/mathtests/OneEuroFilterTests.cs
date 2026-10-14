using System;
using SpatialMac;
using Xunit;

public class OneEuroFilterTests {
    const double Dt = 1.0 / 90.0;

    [Fact] public void FirstSampleIsPassedThrough() {
        Assert.Equal(3.25, new OneEuroFilter().Filter(3.25, Dt));
    }
    [Fact] public void ConstantInputStaysConstant() {
        var f = new OneEuroFilter();
        double y = 0; for (int i = 0; i < 200; i++) y = f.Filter(1.0, Dt);
        Assert.Equal(1.0, y, 9);
    }
    [Fact] public void JitterIsReducedWhenHoldingStill() {
        var f = new OneEuroFilter(minCutoff: 1.0, beta: 0.0);
        var rnd = new Random(7);
        double rawVar = 0, filtVar = 0; int n = 600;
        for (int i = 0; i < n; i++) {
            double noise = (rnd.NextDouble() - 0.5) * 0.01;      // +-5 mm hand tremor
            double y = f.Filter(noise, Dt);
            if (i > 60) { rawVar += noise * noise; filtVar += y * y; }
        }
        Assert.True(filtVar < rawVar * 0.25, $"filtered variance {filtVar} should be well under raw {rawVar}");
    }
    [Fact] public void FastMotionHasLittleLagWhenBetaIsHigh() {
        var smooth = new OneEuroFilter(minCutoff: 1.0, beta: 0.0);
        var adaptive = new OneEuroFilter(minCutoff: 1.0, beta: 5.0);
        double lagSmooth = 0, lagAdaptive = 0;
        for (int i = 0; i < 90; i++) {
            double x = i * Dt * 2.0;                              // 2 m/s sweep
            lagSmooth = x - smooth.Filter(x, Dt);
            lagAdaptive = x - adaptive.Filter(x, Dt);
        }
        Assert.True(lagAdaptive < lagSmooth * 0.5, $"adaptive lag {lagAdaptive} vs fixed {lagSmooth}");
    }
    [Fact] public void ResetForgetsHistory() {
        var f = new OneEuroFilter();
        for (int i = 0; i < 50; i++) f.Filter(10, Dt);
        f.Reset();
        Assert.Equal(-4.0, f.Filter(-4.0, Dt));
    }
    [Fact] public void ZeroDtDoesNotProduceNaN() {
        var f = new OneEuroFilter();
        f.Filter(1, Dt);
        Assert.False(double.IsNaN(f.Filter(2, 0)));
    }
}
