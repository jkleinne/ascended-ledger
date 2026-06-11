using System;

namespace AscendedLedger;

/// <summary>
/// One parsed entry from the game's per-retainer sale-history list (last 20
/// sales). GrossGil is the total sale price; merge matches it against
/// inferred records' GrossGil to avoid unit-price division artifacts.
/// </summary>
public sealed record HistorySale(uint ItemId, int Quantity, long GrossGil, bool IsHq, DateTime SoldAtUtc, string BuyerName);
