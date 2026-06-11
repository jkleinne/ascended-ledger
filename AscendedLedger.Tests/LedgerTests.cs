using System;
using System.Collections.Generic;
using AscendedLedger;
using Xunit;

public class LedgerTests {
    private const ulong OwnerId = 1001UL;
    private const ulong RetainerId = 42UL;
    private const int Rate = 5;
    private static readonly DateTime T0 = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ApplySnapshot_FirstSighting_StoresSnapshotInfersNothing() {
        var ledger = new Ledger();
        var snapshot = new ListingSnapshot(RetainerId, T0, 0, new[] { new Listing(0, 100, 1, 10_000, false) });

        var inferred = ledger.ApplySnapshot(snapshot, Rate, OwnerId);

        Assert.Empty(inferred);
        Assert.Same(snapshot, ledger.LatestSnapshotsByRetainerId[RetainerId]);
    }

    [Fact]
    public void ApplySnapshot_SecondSighting_InfersAndAppendsSales() {
        var ledger = new Ledger();
        ledger.ApplySnapshot(new ListingSnapshot(RetainerId, T0, 0, new[] { new Listing(0, 100, 1, 10_000, false) }), Rate, OwnerId);

        var inferred = ledger.ApplySnapshot(new ListingSnapshot(RetainerId, T1, 9_500, Array.Empty<Listing>()), Rate, OwnerId);

        Assert.Single(inferred);
        Assert.Single(ledger.Sales);
        Assert.Equal(9_500L, ledger.Sales[0].NetGil);
    }

    [Fact]
    public void ApplyHistory_MergesAndReportsChangedCount() {
        var ledger = new Ledger();
        var changed = ledger.ApplyHistory(RetainerId, OwnerId, new[] { new HistorySale(100, 1, 10_000, false, T0, "Buyer Name") }, Rate);

        Assert.Equal(1, changed);
        Assert.Equal(0, ledger.ApplyHistory(RetainerId, OwnerId, new[] { new HistorySale(100, 1, 10_000, false, T0, "Buyer Name") }, Rate));
    }
}
