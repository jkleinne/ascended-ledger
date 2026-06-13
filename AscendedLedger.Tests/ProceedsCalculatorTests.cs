using System;
using AscendedLedger;
using Xunit;

public class ProceedsCalculatorTests {
    [Fact]
    public void Gross_MultipliesQuantityByUnitPrice() {
        Assert.Equal(4_995L, ProceedsCalculator.Gross(5, 999));
    }

    [Fact]
    public void Tax_WithFivePercent_ReturnsFlooredAmount() {
        Assert.Equal(500L, ProceedsCalculator.Tax(10_000, 5));
        Assert.Equal(49L, ProceedsCalculator.Tax(999, 5)); // floor(49.95)
    }

    [Fact]
    public void Net_WithMarketTax_ReturnsTaxedAmount() {
        Assert.Equal(9_500L, ProceedsCalculator.Net(10_000, 5));
    }

    [Fact]
    public void Tax_WithRateOutOfRange_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceedsCalculator.Tax(100, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceedsCalculator.Tax(100, 101));
    }

    [Fact]
    public void Tax_WithNegativeGross_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceedsCalculator.Tax(-1, 5));
    }

    [Fact]
    public void GrossFromNet_IsInverseOfNet_ForCleanValues() {
        Assert.Equal(10_000L, ProceedsCalculator.GrossFromNet(9_500, 5)); // 10000 gross -5% -> 9500 net
        Assert.Equal(612_920L, ProceedsCalculator.GrossFromNet(594_533, 3)); // the live-data case at 3%
    }

    [Fact]
    public void GrossFromNet_ResultIsNeverBelowNet() {
        for (var rate = 0; rate < 100; rate++) {
            Assert.True(ProceedsCalculator.GrossFromNet(12_345, rate) >= 12_345);
        }
    }

    [Fact]
    public void GrossFromNet_WithRateAtOrAbove100_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceedsCalculator.GrossFromNet(100, 100));
    }

    [Fact]
    public void GrossFromNet_WithNegativeNet_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceedsCalculator.GrossFromNet(-1, 5));
    }
}
