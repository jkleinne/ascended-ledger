using System;
using System.Collections.Generic;
using System.Linq;
using AscendedLedger;
using Xunit;

public class LedgerMigrationTests {
    private const ulong Owner = 1001UL;
    private const ulong RetainerId = 42UL;
    private static readonly DateTime Sold = new(2026, 6, 13, 5, 0, 21, DateTimeKind.Utc);
    private static readonly DateTime Detected = new(2026, 6, 13, 5, 36, 46, DateTimeKind.Utc);
    private static readonly IReadOnlyList<Retainer> Retainers =
        new[] { new Retainer(RetainerId, Owner, "R", Town.LimsaLominsa) };
    private static readonly MarketTaxRatesSnapshot Rates =
        new(new Dictionary<Town, int> { [Town.LimsaLominsa] = 3 }, DateTime.UnixEpoch);

    private static SaleRecord InferredRow() => new() {
        OwnerContentId = Owner, RetainerId = RetainerId, ItemId = 52256, Quantity = 20,
        UnitPrice = 30_646, IsHq = false, GrossGil = 612_920, TaxGil = 18_387, NetGil = 594_533,
        IsTaxEstimated = false, SoldAtUtc = Detected, SoldAtPrecision = SoldAtPrecision.DetectedAt,
        Source = SaleSource.Inferred,
    };

    // The v1 defect: GrossGil holds the true net (594_533); NetGil is double-taxed.
    private static SaleRecord BuggyHistoryRow() => new() {
        OwnerContentId = Owner, RetainerId = RetainerId, ItemId = 52256, Quantity = 20,
        UnitPrice = 29_726, IsHq = false, GrossGil = 594_533, TaxGil = 29_726, NetGil = 564_807,
        IsTaxEstimated = true, SoldAtUtc = Sold, SoldAtPrecision = SoldAtPrecision.Exact,
        BuyerName = "Buyer Name", Source = SaleSource.History,
    };

    [Fact]
    public void Migrate_DuplicatePair_CollapsesToSingleMergedRowWithCorrectMoney() {
        var result = LedgerMigration.MigrateSalesV1ToV2(new[] { InferredRow(), BuggyHistoryRow() }, Retainers, Rates);

        var row = Assert.Single(result);
        Assert.Equal(SaleSource.Merged, row.Source);
        Assert.Equal(SoldAtPrecision.Exact, row.SoldAtPrecision);
        Assert.Equal(594_533L, row.NetGil);
        Assert.Equal(612_920L, row.GrossGil);
        Assert.Equal("Buyer Name", row.BuyerName);
        Assert.Equal(Sold, row.SoldAtUtc);
    }

    [Fact]
    public void Migrate_HistoryOnly_RebuildsBreakdownWithExactNet() {
        var result = LedgerMigration.MigrateSalesV1ToV2(new[] { BuggyHistoryRow() }, Retainers, Rates);

        var row = Assert.Single(result);
        Assert.Equal(SaleSource.History, row.Source);
        Assert.Equal(594_533L, row.NetGil);
        Assert.Equal(612_920L, row.GrossGil); // GrossFromNet(594_533, 3) == 612_920
        Assert.Equal(18_387L, row.TaxGil);
        Assert.True(row.IsTaxEstimated);
    }

    [Fact]
    public void Migrate_InferredRow_PassesThroughUntouched() {
        var inferred = InferredRow();
        var result = LedgerMigration.MigrateSalesV1ToV2(new[] { inferred }, Retainers, Rates);
        Assert.Same(inferred, Assert.Single(result));
    }

    [Fact]
    public void Migrate_OrphanRetainer_UsesDefaultRateAndKeepsNetExact() {
        var result = LedgerMigration.MigrateSalesV1ToV2(new[] { BuggyHistoryRow() }, Array.Empty<Retainer>(), null);
        var row = Assert.Single(result);
        Assert.Equal(594_533L, row.NetGil);
        Assert.True(row.GrossGil >= row.NetGil);
    }
}
