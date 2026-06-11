using System;
using System.Collections.Generic;

namespace AscendedLedger;

/// <summary>
/// Live per-town market tax percentages captured from the game, with the
/// game-provided expiry. Towns absent from the dictionary fall back to
/// <see cref="ProceedsCalculator.DefaultTaxRatePercent"/>.
/// </summary>
public sealed record MarketTaxRatesSnapshot(IReadOnlyDictionary<Town, int> RatePercentByTown, DateTime ValidUntilUtc) {
    /// <summary>Returns the captured rate for a town, or the default when unknown.</summary>
    public int RateFor(Town town) =>
        RatePercentByTown.TryGetValue(town, out var rate) ? rate : ProceedsCalculator.DefaultTaxRatePercent;
}
