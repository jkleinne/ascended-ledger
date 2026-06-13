using System;

namespace AscendedLedger;

/// <summary>
/// Integer gil math for sale proceeds. Integer division by 100 floors exactly
/// like the game's tax rounding without double-precision drift on large values.
/// </summary>
public static class ProceedsCalculator {
    /// <summary>Nominal market tax applied when no live rate snapshot is available.</summary>
    public const int DefaultTaxRatePercent = 5;

    /// <summary>Upper bound used to decide whether a gil delta plausibly equals a net amount.</summary>
    public const int MaxTaxRatePercent = 10;

    private const int MinRatePercent = 0;
    private const int MaxRatePercent = 100;

    /// <summary>Total listed price for a stack.</summary>
    public static long Gross(int quantity, long unitPrice) => (long)quantity * unitPrice;

    /// <summary>Market tax withheld from a gross amount at the given percent rate, floored.</summary>
    public static long Tax(long grossGil, int taxRatePercent) {
        ArgumentOutOfRangeException.ThrowIfNegative(grossGil);
        ArgumentOutOfRangeException.ThrowIfLessThan(taxRatePercent, MinRatePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(taxRatePercent, MaxRatePercent);
        return grossGil * taxRatePercent / 100;
    }

    /// <summary>What the seller actually receives after tax.</summary>
    public static long Net(long grossGil, int taxRatePercent) => grossGil - Tax(grossGil, taxRatePercent);

    /// <summary>
    /// Reconstructs the gross from a known net at the given rate — the inverse of
    /// <see cref="Net"/>. Used for history-only sales, where the sale-history packet
    /// gives the after-tax net but no listing supplies the real gross; the result is
    /// an estimate (callers flag IsTaxEstimated) while the net stays exact. Floors
    /// like the game and never returns a gross below <paramref name="netGil"/>.
    /// </summary>
    public static long GrossFromNet(long netGil, int taxRatePercent) {
        ArgumentOutOfRangeException.ThrowIfNegative(netGil);
        ArgumentOutOfRangeException.ThrowIfLessThan(taxRatePercent, MinRatePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(taxRatePercent, MaxRatePercent);
        return netGil * 100 / (100 - taxRatePercent);
    }
}
