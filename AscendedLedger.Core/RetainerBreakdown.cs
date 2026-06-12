namespace AscendedLedger;

/// <summary>Net revenue attributed to one retainer, for the top-retainers table.</summary>
public sealed record RetainerBreakdown(ulong RetainerId, long NetGil, int SaleCount);
