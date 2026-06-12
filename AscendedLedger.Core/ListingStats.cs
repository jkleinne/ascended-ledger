using System;
using System.Collections.Generic;
using System.Linq;

namespace AscendedLedger;

/// <summary>
/// Aggregates current-listing statistics from the latest snapshot of each
/// retainer. Pure: snapshots and rates in, summary out; the clock is injected
/// so age math is testable.
/// </summary>
public static class ListingStats {
    /// <summary>
    /// Computes the listings overview. Rows preserve the input order so the
    /// caller controls display ordering. Ages use each listing's FirstSeenUtc,
    /// falling back to the snapshot's ObservedAtUtc for legacy data, and clamp
    /// at zero against clock skew.
    /// </summary>
    public static ListingsSummary Summarize(IReadOnlyList<ListingStatsInput> retainerMarkets, DateTime nowUtc) {
        if (retainerMarkets.Count == 0) {
            return ListingsSummary.Empty;
        }

        var rows = retainerMarkets.Select(SummarizeRetainer).ToList();
        var allListings = retainerMarkets
            .SelectMany(market => market.Snapshot.Listings.Select(listing => (market.Snapshot, Listing: listing)))
            .ToList();

        return new ListingsSummary {
            ListingCount = allListings.Count,
            TotalUnits = rows.Sum(r => r.Units),
            ExpectedGrossGil = retainerMarkets.Sum(m => m.Snapshot.Listings.Sum(l => ProceedsCalculator.Gross(l.Quantity, l.UnitPrice))),
            ExpectedNetGil = rows.Sum(r => r.ExpectedNetGil),
            TotalRetainerGil = rows.Sum(r => r.RetainerGil),
            SlotsUsed = allListings.Count,
            SlotsTotal = retainerMarkets.Count * ListingSnapshot.MaxSlots,
            Oldest = FindOldest(allListings),
            AverageListingAge = AverageAge(allListings, nowUtc),
            StalestSnapshotUtc = rows.Min(r => r.ObservedAtUtc),
            Rows = rows,
        };
    }

    private static RetainerListingRow SummarizeRetainer(ListingStatsInput input) {
        var (snapshot, ratePercent) = input;
        return new RetainerListingRow(
            snapshot.RetainerId,
            snapshot.Listings.Count,
            snapshot.Listings.Sum(l => (long)l.Quantity),
            snapshot.Listings.Sum(l => ProceedsCalculator.Net(ProceedsCalculator.Gross(l.Quantity, l.UnitPrice), ratePercent)),
            snapshot.RetainerGil,
            snapshot.ObservedAtUtc,
            snapshot.Listings.Count == 0 ? null : snapshot.Listings.Min(l => EffectiveFirstSeen(l, snapshot)));
    }

    private static OldestListing? FindOldest(IReadOnlyList<(ListingSnapshot Snapshot, Listing Listing)> listings) {
        if (listings.Count == 0) {
            return null;
        }

        var (snapshot, listing) = listings.MinBy(pair => EffectiveFirstSeen(pair.Listing, pair.Snapshot));
        return new OldestListing(snapshot.RetainerId, listing.ItemId, listing.IsHq, EffectiveFirstSeen(listing, snapshot));
    }

    private static TimeSpan AverageAge(IReadOnlyList<(ListingSnapshot Snapshot, Listing Listing)> listings, DateTime nowUtc) {
        if (listings.Count == 0) {
            return TimeSpan.Zero;
        }

        // Averaged as double ticks: a hostile far-past firstSeenUtc would
        // overflow a summed-ticks long, and sub-tick precision is irrelevant.
        var averageTicks = listings.Average(pair => (double)Math.Max(0, (nowUtc - EffectiveFirstSeen(pair.Listing, pair.Snapshot)).Ticks));
        return TimeSpan.FromTicks((long)averageTicks);
    }

    private static DateTime EffectiveFirstSeen(Listing listing, ListingSnapshot snapshot) =>
        listing.FirstSeenUtc ?? snapshot.ObservedAtUtc;
}
