using System;

namespace AscendedLedger;

/// <summary>
/// Aggregate view of completed sales for the stats display, produced by
/// <see cref="LedgerStats.Summarize"/>. Period fields are bucketed in the
/// caller's timezone because "today" means the player's day; weeks start
/// Monday, matching <see cref="LedgerStats.NetByPeriod"/>.
/// </summary>
public sealed record SalesSummary {
    /// <summary>Summary of zero sales; every total is zero and optionals are null.</summary>
    public static readonly SalesSummary Empty = new();

    /// <summary>Lifetime net gil across all sales.</summary>
    public long TotalNetGil { get; init; }

    /// <summary>Lifetime gross gil (before market tax).</summary>
    public long TotalGrossGil { get; init; }

    /// <summary>Lifetime market tax withheld.</summary>
    public long TotalTaxGil { get; init; }

    /// <summary>Number of completed sales.</summary>
    public int SaleCount { get; init; }

    /// <summary>Total units sold across all sales.</summary>
    public long UnitsSold { get; init; }

    /// <summary>Net gil earned during the current local day.</summary>
    public long NetToday { get; init; }

    /// <summary>Net gil earned during the current Monday-start local week.</summary>
    public long NetThisWeek { get; init; }

    /// <summary>Net gil earned during the current local month.</summary>
    public long NetThisMonth { get; init; }

    /// <summary>The local day with the highest net total (earliest on tie); null when empty.</summary>
    public PeriodTotal? BestDay { get; init; }

    /// <summary>Lifetime net divided by the number of local days with at least one sale, floored.</summary>
    public long AverageNetPerActiveDay { get; init; }

    /// <summary>Lifetime net divided by the sale count, floored.</summary>
    public long AverageNetPerSale { get; init; }

    /// <summary>UTC moment of the most recent sale; null when empty.</summary>
    public DateTime? LastSaleAtUtc { get; init; }

    /// <summary>Lifetime net gil from high-quality sales.</summary>
    public long HqNetGil { get; init; }

    /// <summary>Number of high-quality sales.</summary>
    public int HqSaleCount { get; init; }

    /// <summary>Lifetime net gil from normal-quality sales.</summary>
    public long NqNetGil { get; init; }

    /// <summary>Number of normal-quality sales.</summary>
    public int NqSaleCount { get; init; }
}
