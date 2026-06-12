namespace AscendedLedger;

/// <summary>
/// One retainer's latest snapshot paired with the market tax rate of its
/// town. The rate arrives as a value so the aggregation stays pure; the
/// retainer identity comes from the snapshot itself.
/// </summary>
public sealed record ListingStatsInput(ListingSnapshot Snapshot, int TaxRatePercent);
