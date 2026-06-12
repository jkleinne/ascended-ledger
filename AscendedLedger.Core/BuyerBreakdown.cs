namespace AscendedLedger;

/// <summary>
/// Net revenue from one buyer, for the top-buyers table. Only sales whose
/// history data carried a buyer name appear here.
/// </summary>
public sealed record BuyerBreakdown(string BuyerName, long NetGil, int SaleCount);
