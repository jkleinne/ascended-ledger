using System;
using System.Collections.Generic;
using System.Linq;

namespace AscendedLedger;

/// <summary>
/// Aggregates net proceeds over time periods. Timestamps are stored UTC (the
/// contract); bucketing converts to the caller's timezone because "today's
/// earnings" means the player's day, not the server's.
/// </summary>
public static class LedgerStats {
    private const int DaysInWeek = 7;

    /// <summary>
    /// Sums net gil per period in the given timezone, newest period first.
    /// Callers filter by character before calling; this aggregates whatever it receives.
    /// </summary>
    public static IReadOnlyList<PeriodTotal> NetByPeriod(IEnumerable<SaleRecord> sales, TimeZoneInfo timeZone, StatsPeriod period) =>
        sales
            .GroupBy(sale => PeriodStart(TimeZoneInfo.ConvertTimeFromUtc(sale.SoldAtUtc, timeZone), period))
            .Select(group => new PeriodTotal(group.Key, group.Sum(sale => sale.NetGil)))
            .OrderByDescending(total => total.PeriodStart)
            .ToList();

    private static DateOnly PeriodStart(DateTime localTime, StatsPeriod period) {
        var date = DateOnly.FromDateTime(localTime);
        return period switch {
            StatsPeriod.Day => date,
            StatsPeriod.Week => date.AddDays(-(((int)date.DayOfWeek + DaysInWeek - 1) % DaysInWeek)),
            StatsPeriod.Month => new DateOnly(date.Year, date.Month, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown stats period."),
        };
    }
}
