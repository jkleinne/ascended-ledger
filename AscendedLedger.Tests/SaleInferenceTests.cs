using System;
using System.Collections.Generic;
using AscendedLedger;
using Xunit;

public class SaleInferenceTests {
    private const ulong OwnerId = 1001UL;
    private const ulong RetainerId = 42UL;
    private const int Rate = 5;
    private static readonly DateTime T0 = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);

    private static ListingSnapshot Snap(DateTime at, long gil, params Listing[] listings) =>
        new(RetainerId, at, gil, listings);

    [Fact]
    public void InferSales_SingleSoldWithExactDelta_ProducesExactRecord() {
        var prev = Snap(T0, 0, new Listing(0, 100, 2, 10_000, false));
        var curr = Snap(T1, 19_000); // gross 20000, tax 1000, net 19000

        var sales = SaleInference.InferSales(prev, curr, Rate, OwnerId);

        var sale = Assert.Single(sales);
        Assert.Equal(20_000L, sale.GrossGil);
        Assert.Equal(19_000L, sale.NetGil);
        Assert.Equal(1_000L, sale.TaxGil);
        Assert.False(sale.IsTaxEstimated);
        Assert.Equal(SaleSource.Inferred, sale.Source);
        Assert.Equal(SoldAtPrecision.DetectedAt, sale.SoldAtPrecision);
        Assert.Equal(T1, sale.SoldAtUtc);
    }

    [Fact]
    public void InferSales_SingleSoldWithPlausibleDelta_UsesDeltaAsNet() {
        // Rate snapshot says 5% but the sale happened under 3%: delta = 19_400.
        var prev = Snap(T0, 0, new Listing(0, 100, 2, 10_000, false));
        var curr = Snap(T1, 19_400);

        var sales = SaleInference.InferSales(prev, curr, Rate, OwnerId);

        var sale = Assert.Single(sales);
        Assert.Equal(19_400L, sale.NetGil);
        Assert.Equal(600L, sale.TaxGil);
        Assert.False(sale.IsTaxEstimated);
    }

    [Fact]
    public void InferSales_MultipleSoldWithMatchingDelta_AllExact() {
        var prev = Snap(T0, 0,
            new Listing(0, 100, 1, 10_000, false),   // net 9500
            new Listing(1, 200, 1, 2_000, true));     // net 1900
        var curr = Snap(T1, 11_400);

        var sales = SaleInference.InferSales(prev, curr, Rate, OwnerId);

        Assert.Equal(2, sales.Count);
        Assert.All(sales, s => Assert.False(s.IsTaxEstimated));
    }

    [Fact]
    public void InferSales_MultipleSoldWithShortDelta_AllFlaggedEstimated() {
        var prev = Snap(T0, 0,
            new Listing(0, 100, 1, 10_000, false),
            new Listing(1, 200, 1, 2_000, true));
        var curr = Snap(T1, 5_000); // owner withdrew gil between visits

        var sales = SaleInference.InferSales(prev, curr, Rate, OwnerId);

        Assert.Equal(2, sales.Count);
        Assert.All(sales, s => Assert.True(s.IsTaxEstimated));
    }

    [Fact]
    public void InferSales_GilUnchanged_IsRemovalNotSale() {
        var prev = Snap(T0, 500, new Listing(0, 100, 1, 10_000, false));
        var curr = Snap(T1, 500);

        Assert.Empty(SaleInference.InferSales(prev, curr, Rate, OwnerId));
    }

    [Fact]
    public void InferSales_NegativeDelta_ProducesNoRecords() {
        var prev = Snap(T0, 50_000, new Listing(0, 100, 1, 10_000, false));
        var curr = Snap(T1, 10_000);

        Assert.Empty(SaleInference.InferSales(prev, curr, Rate, OwnerId));
    }

    [Fact]
    public void InferSales_PriceOnlyChange_ProducesNoRecords() {
        var prev = Snap(T0, 0, new Listing(0, 100, 1, 10_000, false));
        var curr = Snap(T1, 0, new Listing(0, 100, 1, 9_900, false));

        Assert.Empty(SaleInference.InferSales(prev, curr, Rate, OwnerId));
    }

    [Fact]
    public void InferSales_ZeroPriceListingVanished_ProducesNoRecords() {
        // Garbage-memory defense: a zero-priced "listing" cannot be a sale.
        var prev = Snap(T0, 0, new Listing(0, 100, 1, 0, false));
        var curr = Snap(T1, 5_000);

        Assert.Empty(SaleInference.InferSales(prev, curr, Rate, OwnerId));
    }

    [Fact]
    public void InferSales_SlotShuffleSameContent_ProducesNoRecords() {
        var prev = Snap(T0, 0,
            new Listing(0, 100, 1, 10_000, false),
            new Listing(1, 200, 3, 500, false));
        var curr = Snap(T1, 0,
            new Listing(0, 200, 3, 500, false),
            new Listing(1, 100, 1, 10_000, false));

        Assert.Empty(SaleInference.InferSales(prev, curr, Rate, OwnerId));
    }

    [Fact]
    public void InferSales_DifferentRetainers_Throws() {
        var prev = Snap(T0, 0);
        var curr = new ListingSnapshot(RetainerId + 1, T1, 0, Array.Empty<Listing>());

        Assert.Throws<ArgumentException>(() => SaleInference.InferSales(prev, curr, Rate, OwnerId));
    }
}
