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

    /// <summary>
    /// Computes the earnings-KPI summary over the given sales. Bucketing uses
    /// <paramref name="timeZone"/> and <paramref name="nowUtc"/> so "today",
    /// "this week" (Monday start), and "this month" mean the player's
    /// calendar; both are injected for testability.
    /// </summary>
    public static SalesSummary Summarize(IEnumerable<SaleRecord> sales, TimeZoneInfo timeZone, DateTime nowUtc) {
        var all = sales as IReadOnlyList<SaleRecord> ?? sales.ToList();
        if (all.Count == 0) {
            return SalesSummary.Empty;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone));
        var weekStart = StartOfWeek(today);
        var netByDay = NetByPeriod(all, timeZone, StatsPeriod.Day);
        var totalNet = all.Sum(s => s.NetGil);
        var hqNet = all.Where(s => s.IsHq).Sum(s => s.NetGil);
        var hqCount = all.Count(s => s.IsHq);

        return new SalesSummary {
            TotalNetGil = totalNet,
            TotalGrossGil = all.Sum(s => s.GrossGil),
            TotalTaxGil = all.Sum(s => s.TaxGil),
            SaleCount = all.Count,
            UnitsSold = all.Sum(s => (long)s.Quantity),
            NetToday = netByDay.Where(t => t.PeriodStart == today).Sum(t => t.NetGil),
            NetThisWeek = netByDay.Where(t => t.PeriodStart >= weekStart && t.PeriodStart < weekStart.AddDays(DaysInWeek)).Sum(t => t.NetGil),
            NetThisMonth = netByDay.Where(t => t.PeriodStart.Year == today.Year && t.PeriodStart.Month == today.Month).Sum(t => t.NetGil),
            BestDay = netByDay.OrderByDescending(t => t.NetGil).ThenBy(t => t.PeriodStart).First(),
            AverageNetPerActiveDay = totalNet / netByDay.Count,
            AverageNetPerSale = totalNet / all.Count,
            LastSaleAtUtc = all.Max(s => s.SoldAtUtc),
            HqNetGil = hqNet,
            HqSaleCount = hqCount,
            NqNetGil = totalNet - hqNet,
            NqSaleCount = all.Count - hqCount,
        };
    }

    private static DateOnly PeriodStart(DateTime localTime, StatsPeriod period) {
        var date = DateOnly.FromDateTime(localTime);
        return period switch {
            StatsPeriod.Day => date,
            StatsPeriod.Week => StartOfWeek(date),
            StatsPeriod.Month => new DateOnly(date.Year, date.Month, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown stats period."),
        };
    }

    /// <summary>Monday that starts the ISO week containing <paramref name="date"/>.</summary>
    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + DaysInWeek - 1) % DaysInWeek));
}
