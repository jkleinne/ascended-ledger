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
    public void ApplySnapshot_FirstSighting_StoresStampedSnapshotInfersNothing() {
        var ledger = new Ledger();
        var snapshot = new ListingSnapshot(RetainerId, T0, 0, new[] { new Listing(0, 100, 1, 10_000, false) });

        var inferred = ledger.ApplySnapshot(snapshot, Rate, OwnerId);

        Assert.Empty(inferred);
        var stored = ledger.LatestSnapshotsByRetainerId[RetainerId];
        Assert.Equal(T0, Assert.Single(stored.Listings).FirstSeenUtc);
    }

    [Fact]
    public void ApplySnapshot_SurvivingListing_KeepsFirstSeenAcrossVisits() {
        var ledger = new Ledger();
        ledger.ApplySnapshot(new ListingSnapshot(RetainerId, T0, 0, new[] { new Listing(0, 100, 1, 10_000, false) }), Rate, OwnerId);

        ledger.ApplySnapshot(new ListingSnapshot(RetainerId, T1, 0, new[] { new Listing(7, 100, 1, 10_000, false) }), Rate, OwnerId);

        Assert.Equal(T0, Assert.Single(ledger.LatestSnapshotsByRetainerId[RetainerId].Listings).FirstSeenUtc);
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

    [Fact]
    public void Revision_IncrementsOnEveryMutation() {
        var ledger = new Ledger();
        Assert.Equal(0L, ledger.Revision);

        ledger.UpsertCharacter(new Character(1, "A", "W"));
        Assert.Equal(1L, ledger.Revision);

        ledger.UpsertRetainer(new Retainer(RetainerId, 1, "R", Town.LimsaLominsa));
        Assert.Equal(2L, ledger.Revision);

        ledger.SetTaxRates(new MarketTaxRatesSnapshot(new Dictionary<Town, int>(), T0));
        Assert.Equal(3L, ledger.Revision);

        ledger.ApplySnapshot(new ListingSnapshot(RetainerId, T0, 0, Array.Empty<Listing>()), Rate, OwnerId);
        Assert.Equal(4L, ledger.Revision);

        ledger.ApplyHistory(RetainerId, OwnerId, Array.Empty<HistorySale>(), Rate);
        Assert.Equal(5L, ledger.Revision);
    }

    [Fact]
    public void Restore_LeavesRevisionAtBaselineZero() {
        var ledger = Ledger.Restore(
            new[] { new Character(1, "A", "W") },
            new[] { new Retainer(RetainerId, 1, "R", Town.LimsaLominsa) },
            Array.Empty<ListingSnapshot>(),
            Array.Empty<SaleRecord>(),
            new MarketTaxRatesSnapshot(new Dictionary<Town, int>(), T0));

        Assert.Equal(0L, ledger.Revision);
    }
}
