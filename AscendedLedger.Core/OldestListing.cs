using System;

namespace AscendedLedger;

/// <summary>The single longest-listed item across all retainers, for the stats headline.</summary>
public sealed record OldestListing(ulong RetainerId, uint ItemId, bool IsHq, DateTime FirstSeenUtc);
