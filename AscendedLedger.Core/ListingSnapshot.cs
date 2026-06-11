using System;
using System.Collections.Generic;

namespace AscendedLedger;

/// <summary>
/// Point-in-time view of one retainer's market: its listings and its held gil.
/// The gil figure is what lets inference distinguish a sale (gil rose) from
/// the player removing an item (gil unchanged).
/// </summary>
public sealed record ListingSnapshot(ulong RetainerId, DateTime ObservedAtUtc, long RetainerGil, IReadOnlyList<Listing> Listings) {
    /// <summary>A retainer market holds at most this many slots (game cap).</summary>
    public const int MaxSlots = 20;
}
