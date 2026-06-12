namespace AscendedLedger;

/// <summary>
/// Net revenue of one (item, quality) pair, for the top-items table. HQ and
/// NQ are distinct entries because they sell at different price profiles.
/// </summary>
public sealed record ItemBreakdown(uint ItemId, bool IsHq, long NetGil, int SaleCount, long Units);
