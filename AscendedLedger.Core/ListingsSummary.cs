using System;
using System.Collections.Generic;

namespace AscendedLedger;

/// <summary>
/// Aggregate view of all current listings, produced by
/// <see cref="ListingStats.Summarize"/>. "Expected" figures assume every
/// listing sells at its asking price under the supplied tax rates.
/// </summary>
public sealed record ListingsSummary {
    /// <summary>Summary of zero retainers; every total is zero and optionals are null.</summary>
    public static readonly ListingsSummary Empty = new();

    /// <summary>Occupied market slots across all retainers.</summary>
    public int ListingCount { get; init; }

    /// <summary>Total units across all listings.</summary>
    public long TotalUnits { get; init; }

    /// <summary>Sum of asking prices before tax.</summary>
    public long ExpectedGrossGil { get; init; }

    /// <summary>Sum of asking prices after each retainer's town tax.</summary>
    public long ExpectedNetGil { get; init; }

    /// <summary>Gil currently sitting on the retainers themselves.</summary>
    public long TotalRetainerGil { get; init; }

    /// <summary>Equals <see cref="ListingCount"/>; each listing occupies one slot.</summary>
    public int SlotsUsed { get; init; }

    /// <summary>Retainers-with-snapshots × <see cref="ListingSnapshot.MaxSlots"/>.</summary>
    public int SlotsTotal { get; init; }

    /// <summary>The longest-listed item, or null when no listings exist.</summary>
    public OldestListing? Oldest { get; init; }

    /// <summary>Mean age across all listings (effective first-seen), zero when no listings.</summary>
    public TimeSpan AverageListingAge { get; init; }

    /// <summary>Observation time of the least recently visited retainer; null when empty.</summary>
    public DateTime? StalestSnapshotUtc { get; init; }

    /// <summary>One row per retainer, in input order.</summary>
    public IReadOnlyList<RetainerListingRow> Rows { get; init; } = Array.Empty<RetainerListingRow>();
}
