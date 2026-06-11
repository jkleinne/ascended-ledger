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
}
