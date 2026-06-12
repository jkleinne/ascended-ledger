using System;

namespace AscendedLedger;

/// <summary>
/// Per-retainer line of the listings overview. OldestFirstSeenUtc is null
/// when the retainer currently has no listings.
/// </summary>
public sealed record RetainerListingRow(ulong RetainerId, int ListingCount, long Units, long ExpectedNetGil, long RetainerGil, DateTime ObservedAtUtc, DateTime? OldestFirstSeenUtc);
