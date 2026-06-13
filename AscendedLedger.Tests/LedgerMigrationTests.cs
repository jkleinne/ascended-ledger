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

    [Fact]
    public void Migrate_TwoRetainers_ReconcilesEachIndependently() {
        // Each retainer has an inferred row plus its buggy history twin; the
        // per-retainer grouping must collapse each pair without cross-contaminating.
        const ulong retainer2 = 99UL;
        var inferred2 = new SaleRecord {
            OwnerContentId = Owner, RetainerId = retainer2, ItemId = 7984, Quantity = 1,
            UnitPrice = 77_678, IsHq = false, GrossGil = 77_678, TaxGil = 2_330, NetGil = 75_348,
            IsTaxEstimated = false, SoldAtUtc = Detected, SoldAtPrecision = SoldAtPrecision.DetectedAt,
            Source = SaleSource.Inferred,
        };
        var history2 = new SaleRecord {
            OwnerContentId = Owner, RetainerId = retainer2, ItemId = 7984, Quantity = 1,
            UnitPrice = 75_348, IsHq = false, GrossGil = 75_348, TaxGil = 3_767, NetGil = 71_581,
            IsTaxEstimated = true, SoldAtUtc = Sold, SoldAtPrecision = SoldAtPrecision.Exact,
            BuyerName = "Other Buyer", Source = SaleSource.History,
        };
        var retainers = new[] {
            new Retainer(RetainerId, Owner, "R", Town.LimsaLominsa),
            new Retainer(retainer2, Owner, "R2", Town.LimsaLominsa),
        };

        var result = LedgerMigration.MigrateSalesV1ToV2(
            new[] { InferredRow(), BuggyHistoryRow(), inferred2, history2 }, retainers, Rates);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(SaleSource.Merged, r.Source));
        var first = Assert.Single(result, r => r.RetainerId == RetainerId);
        Assert.Equal(594_533L, first.NetGil);
        Assert.Equal(612_920L, first.GrossGil);
        var second = Assert.Single(result, r => r.RetainerId == retainer2);
        Assert.Equal(75_348L, second.NetGil);
        Assert.Equal(77_678L, second.GrossGil);
    }

    [Fact]
    public void Migrate_MergedRow_PassesThroughUntouched() {
        // Only Source=History rows carry the v1 defect; an already-Merged row is
        // correct and must survive migration by reference, untouched.
        var merged = new SaleRecord {
            OwnerContentId = Owner, RetainerId = RetainerId, ItemId = 52256, Quantity = 20,
            UnitPrice = 30_646, IsHq = false, GrossGil = 612_920, TaxGil = 18_387, NetGil = 594_533,
            IsTaxEstimated = false, SoldAtUtc = Sold, SoldAtPrecision = SoldAtPrecision.Exact,
            BuyerName = "Buyer Name", Source = SaleSource.Merged,
        };

        var result = LedgerMigration.MigrateSalesV1ToV2(new[] { merged }, Retainers, Rates);

        Assert.Same(merged, Assert.Single(result));
    }
}
