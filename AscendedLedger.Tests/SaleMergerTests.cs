using System;
using System.Collections.Generic;
using System.Linq;
using AscendedLedger;
using Xunit;

public class SaleMergerTests {
    private const ulong OwnerId = 1001UL;
    private const ulong RetainerId = 42UL;
    private const int Rate = 5;
    private static readonly DateTime Sold = new(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Detected = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SaleRecord Inferred(DateTime detectedAt, long gross = 20_000, bool estimated = false) => new() {
        OwnerContentId = OwnerId,
        RetainerId = RetainerId,
        ItemId = 100,
        Quantity = 2,
        UnitPrice = 10_000,
        IsHq = false,
        GrossGil = gross,
        TaxGil = 1_000,
        NetGil = gross - 1_000,
        IsTaxEstimated = estimated,
        SoldAtUtc = detectedAt,
        SoldAtPrecision = SoldAtPrecision.DetectedAt,
        Source = SaleSource.Inferred,
    };

    private static HistorySale Entry(DateTime soldAt, long gross = 20_000, string buyer = "Some Buyer") =>
        new(100, 2, gross, false, soldAt, buyer);

    [Fact]
    public void Merge_MatchingInferredRecord_UpgradesToMerged() {
        var merged = SaleMerger.Merge(new[] { Inferred(Detected) }, new[] { Entry(Sold) }, OwnerId, RetainerId, Rate);

        var record = Assert.Single(merged);
        Assert.Equal(SaleSource.Merged, record.Source);
        Assert.Equal(SoldAtPrecision.Exact, record.SoldAtPrecision);
        Assert.Equal(Sold, record.SoldAtUtc);
        Assert.Equal("Some Buyer", record.BuyerName);
        Assert.Equal(19_000L, record.NetGil);
        Assert.False(record.IsTaxEstimated);
    }

    [Fact]
    public void Merge_MultipleCandidates_UpgradesClosestDetection() {
        var earlier = Inferred(Detected);
        var later = Inferred(Detected.AddDays(3));

        var merged = SaleMerger.Merge(new[] { later, earlier }, new[] { Entry(Sold) }, OwnerId, RetainerId, Rate);

        Assert.Equal(SaleSource.Merged, merged.Single(r => r.SoldAtUtc == Sold).Source);
        Assert.Contains(merged, r => r.Source == SaleSource.Inferred); // the later one untouched
    }

    [Fact]
    public void Merge_UnmatchedEntry_InsertsHistoryRecordWithEstimatedTax() {
        var merged = SaleMerger.Merge(Array.Empty<SaleRecord>(), new[] { Entry(Sold) }, OwnerId, RetainerId, Rate);

        var record = Assert.Single(merged);
        Assert.Equal(SaleSource.History, record.Source);
        Assert.Equal(SoldAtPrecision.Exact, record.SoldAtPrecision);
        Assert.Equal(20_000L, record.GrossGil);
        Assert.Equal(19_000L, record.NetGil);
        Assert.True(record.IsTaxEstimated);
        Assert.Equal(10_000L, record.UnitPrice);
    }

    [Fact]
    public void Merge_SameEntriesTwice_IsIdempotent() {
        var once = SaleMerger.Merge(new[] { Inferred(Detected) }, new[] { Entry(Sold) }, OwnerId, RetainerId, Rate);
        var twice = SaleMerger.Merge(once, new[] { Entry(Sold) }, OwnerId, RetainerId, Rate);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Merge_HistoryAfterDetection_DoesNotMatch() {
        var merged = SaleMerger.Merge(new[] { Inferred(Detected) }, new[] { Entry(Detected.AddHours(1)) }, OwnerId, RetainerId, Rate);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, r => r.Source == SaleSource.Inferred);
        Assert.Contains(merged, r => r.Source == SaleSource.History);
    }

    [Fact]
    public void Merge_EstimatedInferredRecord_KeepsEstimatedFlagAfterUpgrade() {
        var merged = SaleMerger.Merge(new[] { Inferred(Detected, estimated: true) }, new[] { Entry(Sold) }, OwnerId, RetainerId, Rate);

        var record = Assert.Single(merged);
        Assert.Equal(SaleSource.Merged, record.Source);
        Assert.True(record.IsTaxEstimated);
    }

    [Fact]
    public void Merge_EmptyBuyerAndEpochZero_StillDedupes() {
        var entry = new HistorySale(100, 2, 20_000, false, DateTime.UnixEpoch, string.Empty);
        var once = SaleMerger.Merge(Array.Empty<SaleRecord>(), new[] { entry }, OwnerId, RetainerId, Rate);
        var twice = SaleMerger.Merge(once, new[] { entry }, OwnerId, RetainerId, Rate);

        Assert.Single(twice);
    }

    [Fact]
    public void Merge_InputList_IsNotMutated() {
        var original = Inferred(Detected);
        var existing = new List<SaleRecord> { original };

        SaleMerger.Merge(existing, new[] { Entry(Sold) }, OwnerId, RetainerId, Rate);

        var untouched = Assert.Single(existing);
        Assert.Same(original, untouched);
        Assert.Equal(SaleSource.Inferred, untouched.Source);
        Assert.Null(untouched.BuyerName);
    }

    [Fact]
    public void Merge_HqMismatch_DoesNotUpgrade() {
        var hqEntry = new HistorySale(100, 2, 20_000, true, Sold, "Some Buyer");

        var merged = SaleMerger.Merge(new[] { Inferred(Detected) }, new[] { hqEntry }, OwnerId, RetainerId, Rate);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, r => r.Source == SaleSource.Inferred && !r.IsHq);
        Assert.Contains(merged, r => r.Source == SaleSource.History && r.IsHq);
    }
}
