namespace AscendedLedger;

/// <summary>
/// One occupied retainer market slot at observation time. Slot is kept for
/// traceability only; sale inference matches listings by content, not slot,
/// so the game reshuffling slots cannot fake a sale.
/// </summary>
public sealed record Listing(int Slot, uint ItemId, int Quantity, long UnitPrice, bool IsHq);
