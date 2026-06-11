using System;

namespace AscendedLedger;

/// <summary>
/// One occupied retainer market slot at observation time. Slot is kept for
/// traceability only; sale inference matches listings by content, not slot,
/// so the game reshuffling slots cannot fake a sale. FirstSeenUtc is the
/// earliest snapshot observation at which this exact content was continuously
/// observed on its retainer; null only on legacy data persisted before the
/// field existed. A reprice or quantity change starts a new identity and so
/// a fresh first-seen.
/// </summary>
public sealed record Listing(int Slot, uint ItemId, int Quantity, long UnitPrice, bool IsHq, DateTime? FirstSeenUtc = null) {
    /// <summary>
    /// Identity for matching listings across snapshots. Excludes Slot because
    /// the game reshuffles slots, and FirstSeenUtc because it is derived
    /// bookkeeping; a price or quantity change is a new identity.
    /// </summary>
    public (uint ItemId, int Quantity, long UnitPrice, bool IsHq) ContentKey() => (ItemId, Quantity, UnitPrice, IsHq);
}
