using System;
using AscendedLedger;
using Xunit;

public class ListingStatsTests {
    private static readonly DateTime Now = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T0 = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);

    private static ListingStatsInput Market(ulong retainerId, DateTime observedAt, long gil, params Listing[] listings) =>
        new(new ListingSnapshot(retainerId, observedAt, gil, listings), 5);

    [Fact]
    public void Summarize_Empty_ReturnsEmptySummary() {
        var summary = ListingStats.Summarize(Array.Empty<ListingStatsInput>(), Now);

        Assert.Equal(ListingsSummary.Empty, summary);
        Assert.Null(summary.Oldest);
        Assert.Empty(summary.Rows);
    }

    [Fact]
    public void Summarize_MixedTaxRates_ComputesExpectedNetPerRetainer() {
        var inputs = new[] {
            new ListingStatsInput(new ListingSnapshot(1, T1, 0, new[] { new Listing(0, 100, 1, 10_000, false, T1) }), 5),
            new ListingStatsInput(new ListingSnapshot(2, T1, 0, new[] { new Listing(0, 100, 1, 10_000, false, T1) }), 0),
        };

        var summary = ListingStats.Summarize(inputs, Now);

        Assert.Equal(20_000, summary.ExpectedGrossGil);
        Assert.Equal(19_500, summary.ExpectedNetGil); // 9,500 at 5% + 10,000 at 0%
        Assert.Equal(9_500, summary.Rows[0].ExpectedNetGil);
        Assert.Equal(10_000, summary.Rows[1].ExpectedNetGil);
    }

    [Fact]
    public void Summarize_OldestListing_FoundAcrossRetainers() {
        var inputs = new[] {
            Market(1, T1, 0, new Listing(0, 100, 1, 10_000, false, T1)),
            Market(2, T1, 0, new Listing(0, 200, 2, 5_000, true, T0)),
        };

        var summary = ListingStats.Summarize(inputs, Now);

        Assert.Equal(new OldestListing(2, 200, true, T0), summary.Oldest);
        Assert.Equal(T0, summary.Rows[1].OldestFirstSeenUtc);
    }

    [Fact]
    public void Summarize_LegacyNullFirstSeen_FallsBackToSnapshotObservation() {
        var summary = ListingStats.Summarize(new[] { Market(1, T0, 0, new Listing(0, 100, 1, 10_000, false)) }, Now);

        Assert.Equal(new OldestListing(1, 100, false, T0), summary.Oldest);
    }

    [Fact]
    public void Summarize_FutureFirstSeen_ClampsAgeToZero() {
        var future = Now.AddHours(2);
        var summary = ListingStats.Summarize(new[] { Market(1, future, 0, new Listing(0, 100, 1, 10_000, false, future)) }, Now);

        Assert.Equal(TimeSpan.Zero, summary.AverageListingAge);
    }

    [Fact]
    public void Summarize_ExtremePastFirstSeen_DoesNotThrowOrOverflow() {
        var listings = new Listing[ListingSnapshot.MaxSlots];
        for (var slot = 0; slot < listings.Length; slot++) {
            listings[slot] = new Listing(slot, 100, 1, 10_000, false, DateTime.MinValue);
        }

        var summary = ListingStats.Summarize(new[] { Market(1, T1, 0, listings) }, Now);

        Assert.True(summary.AverageListingAge > TimeSpan.FromDays(365 * 100));
    }

    [Fact]
    public void Summarize_SlotsAndUnits_CountAcrossRetainers() {
        var inputs = new[] {
            Market(1, T1, 100, new Listing(0, 100, 3, 10_000, false, T1), new Listing(1, 100, 2, 10_000, false, T1)),
            Market(2, T0, 50),
        };

        var summary = ListingStats.Summarize(inputs, Now);

        Assert.Equal(2, summary.ListingCount);
        Assert.Equal(5, summary.TotalUnits);
        Assert.Equal(2, summary.SlotsUsed);
        Assert.Equal(2 * ListingSnapshot.MaxSlots, summary.SlotsTotal);
        Assert.Equal(150, summary.TotalRetainerGil);
        Assert.Equal(T0, summary.StalestSnapshotUtc);
    }

    [Fact]
    public void Summarize_EmptySnapshotRetainer_StillContributesRowAndGil() {
        var summary = ListingStats.Summarize(new[] { Market(1, T1, 75_000) }, Now);

        var row = Assert.Single(summary.Rows);
        Assert.Equal(0, row.ListingCount);
        Assert.Equal(75_000, row.RetainerGil);
        Assert.Null(row.OldestFirstSeenUtc);
        Assert.Equal(TimeSpan.Zero, summary.AverageListingAge);
        Assert.Null(summary.Oldest);
    }
}
