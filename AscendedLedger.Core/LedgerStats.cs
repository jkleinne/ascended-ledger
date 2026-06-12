using System;
using System.Collections.Generic;
using System.Linq;

namespace AscendedLedger;

/// <summary>
/// Aggregates completed-sale statistics: period totals, the KPI summary, and
/// top-N breakdowns. Timestamps are stored UTC (the contract); bucketing
/// converts to the caller's timezone because "today's earnings" means the
/// player's day, not the server's.
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

    /// <summary>
    /// Top items by net gil, grouped by (item, quality) to match how the rest
    /// of the UI distinguishes HQ from NQ. Ties order by item id then quality
    /// for deterministic display.
    /// </summary>
    public static IReadOnlyList<ItemBreakdown> TopItems(IEnumerable<SaleRecord> sales, int count) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return sales
            .GroupBy(s => (s.ItemId, s.IsHq))
            .Select(g => new ItemBreakdown(g.Key.ItemId, g.Key.IsHq, g.Sum(s => s.NetGil), g.Count(), g.Sum(s => (long)s.Quantity)))
            .OrderByDescending(b => b.NetGil)
            .ThenBy(b => b.ItemId)
            .ThenBy(b => b.IsHq)
            .Take(count)
            .ToList();
    }

    /// <summary>Top retainers by net gil; ties order by retainer id.</summary>
    public static IReadOnlyList<RetainerBreakdown> TopRetainers(IEnumerable<SaleRecord> sales, int count) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return sales
            .GroupBy(s => s.RetainerId)
            .Select(g => new RetainerBreakdown(g.Key, g.Sum(s => s.NetGil), g.Count()))
            .OrderByDescending(b => b.NetGil)
            .ThenBy(b => b.RetainerId)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Top buyers by net gil. Sales without a buyer name (inferred-only
    /// records) are excluded rather than lumped into a fake "unknown" buyer.
    /// </summary>
    public static IReadOnlyList<BuyerBreakdown> TopBuyers(IEnumerable<SaleRecord> sales, int count) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return sales
            .Where(s => !string.IsNullOrEmpty(s.BuyerName))
            .GroupBy(s => s.BuyerName!)
            .Select(g => new BuyerBreakdown(g.Key, g.Sum(s => s.NetGil), g.Count()))
            .OrderByDescending(b => b.NetGil)
            .ThenBy(b => b.BuyerName, StringComparer.Ordinal)
            .Take(count)
            .ToList();
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
