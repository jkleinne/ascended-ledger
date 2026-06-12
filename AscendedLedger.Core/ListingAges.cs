using System;
using System.Collections.Generic;
using System.Linq;

namespace AscendedLedger;

/// <summary>
/// Stamps FirstSeenUtc continuity timestamps across consecutive snapshots of
/// one retainer. Content-matched listings keep their original first-seen so
/// "how long has this sat unsold at this price" survives re-observation;
/// everything else starts fresh at the current observation.
/// </summary>
public static class ListingAges {
    /// <summary>
    /// Returns <paramref name="current"/> with every listing's FirstSeenUtc
    /// populated. Matched listings (multiset by content, FIFO order) inherit
    /// the previous listing's FirstSeenUtc, falling back to the previous
    /// snapshot's ObservedAtUtc for legacy data; unmatched listings get the
    /// current snapshot's ObservedAtUtc. A null <paramref name="previous"/>
    /// (first sighting of the retainer) stamps everything as new.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the snapshots belong to different retainers. Mismatched
    /// snapshots are a caller bug and must fail loudly so corrupt ages are
    /// never written to the ledger.
    /// </exception>
    public static ListingSnapshot CarryForward(ListingSnapshot? previous, ListingSnapshot current) {
        if (previous is not null && previous.RetainerId != current.RetainerId) {
            throw new ArgumentException(
                $"Snapshot retainer mismatch: previous={previous.RetainerId}, current={current.RetainerId}.");
        }

        var inheritable = new Dictionary<(uint ItemId, int Quantity, long UnitPrice, bool IsHq), Queue<DateTime>>();
        if (previous is not null) {
            foreach (var listing in previous.Listings) {
                if (!inheritable.TryGetValue(listing.ContentKey(), out var queue)) {
                    queue = new Queue<DateTime>();
                    inheritable[listing.ContentKey()] = queue;
                }

                queue.Enqueue(listing.FirstSeenUtc ?? previous.ObservedAtUtc);
            }
        }

        var stamped = current.Listings
            .Select(listing =>
                inheritable.TryGetValue(listing.ContentKey(), out var queue) && queue.Count > 0
                    ? listing with { FirstSeenUtc = queue.Dequeue() }
                    : listing with { FirstSeenUtc = current.ObservedAtUtc })
            .ToList();
        return current with { Listings = stamped };
    }
}
