using System;

namespace AscendedLedger;

/// <summary>
/// One parsed entry from the game's per-retainer sale-history list (last 20
/// sales). NetGil is the after-tax deposit the seller received — the packet's
/// price field is net, not gross. Merge matches it against inferred records'
/// NetGil so the comparison is independent of the tax rate.
/// </summary>
public sealed record HistorySale(uint ItemId, int Quantity, long NetGil, bool IsHq, DateTime SoldAtUtc, string BuyerName);
