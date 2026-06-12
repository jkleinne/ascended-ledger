using System;
using System.Collections.Generic;
using AscendedLedger;
using Xunit;

public class LedgerStatsTests {
    private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("p2", TimeSpan.FromHours(2), "p2", "p2");

    private static SaleRecord Sale(DateTime soldAtUtc, long net, bool isHq = false, int quantity = 1) => new() {
        OwnerContentId = 1,
        RetainerId = 1,
        ItemId = 100,
        Quantity = quantity,
        UnitPrice = net,
        IsHq = isHq,
        GrossGil = net,
        TaxGil = 0,
        NetGil = net,
        IsTaxEstimated = false,
        SoldAtUtc = soldAtUtc,
        SoldAtPrecision = SoldAtPrecision.Exact,
        Source = SaleSource.History,
    };

    [Fact]
    public void NetByPeriod_Day_BucketsAtLocalMidnight() {
        var sales = new[] {
            Sale(new DateTime(2026, 6, 1, 23, 30, 0, DateTimeKind.Utc), 100), // 01:30 local on June 2
            Sale(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), 50),
        };

        var totals = LedgerStats.NetByPeriod(sales, PlusTwo, StatsPeriod.Day);

        Assert.Equal(2, totals.Count);
        Assert.Equal(new PeriodTotal(new DateOnly(2026, 6, 2), 100), totals[0]);
        Assert.Equal(new PeriodTotal(new DateOnly(2026, 6, 1), 50), totals[1]);
    }

    [Fact]
    public void NetByPeriod_Week_StartsMonday() {
        // 2026-06-03 is a Wednesday; its ISO week starts Monday 2026-06-01.
        var totals = LedgerStats.NetByPeriod(
            new[] { Sale(new DateTime(2026, 6, 3, 10, 0, 0, DateTimeKind.Utc), 70) },
            TimeZoneInfo.Utc,
            StatsPeriod.Week);

        Assert.Equal(new PeriodTotal(new DateOnly(2026, 6, 1), 70), Assert.Single(totals));
    }

    [Fact]
    public void NetByPeriod_Month_SumsWithinMonth() {
        var sales = new[] {
            Sale(new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc), 70),
            Sale(new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc), 30),
            Sale(new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc), 5),
        };

        var totals = LedgerStats.NetByPeriod(sales, TimeZoneInfo.Utc, StatsPeriod.Month);

        Assert.Equal(2, totals.Count);
        Assert.Equal(new PeriodTotal(new DateOnly(2026, 6, 1), 100), totals[0]);
        Assert.Equal(new PeriodTotal(new DateOnly(2026, 5, 1), 5), totals[1]);
    }

    [Fact]
    public void NetByPeriod_LargeValues_AccumulatesInLong() {
        // Two near-cap sales (~1e13 gil each) exceed int range; sums must stay in long.
        var sales = new[] {
            Sale(new DateTime(2026, 6, 3, 1, 0, 0, DateTimeKind.Utc), 9_999_000_000_000),
            Sale(new DateTime(2026, 6, 3, 2, 0, 0, DateTimeKind.Utc), 9_999_000_000_000),
        };

        var totals = LedgerStats.NetByPeriod(sales, TimeZoneInfo.Utc, StatsPeriod.Day);

        Assert.Equal(19_998_000_000_000L, Assert.Single(totals).NetGil);
    }

    [Fact]
    public void NetByPeriod_EmptyInput_ReturnsEmpty() {
        Assert.Empty(LedgerStats.NetByPeriod(Array.Empty<SaleRecord>(), TimeZoneInfo.Utc, StatsPeriod.Day));
    }

    [Fact]
    public void Summarize_Empty_ReturnsEmptySummary() {
        var summary = LedgerStats.Summarize(Array.Empty<SaleRecord>(), TimeZoneInfo.Utc, new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(SalesSummary.Empty, summary);
        Assert.Null(summary.BestDay);
        Assert.Null(summary.LastSaleAtUtc);
    }

    [Fact]
    public void Summarize_BucketsTodayWeekMonthInCallerTimezone() {
        // Local now is 2026-06-11 01:30 (+2): today = Thu 06-11, week starts Mon 06-08, month = June.
        var nowUtc = new DateTime(2026, 6, 10, 23, 30, 0, DateTimeKind.Utc);
        var sales = new[] {
            Sale(new DateTime(2026, 6, 10, 22, 30, 0, DateTimeKind.Utc), 100), // local 06-11 00:30 → today
            Sale(new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc), 40),    // Monday → this week
            Sale(new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc), 10),    // Sunday → last week, this month
            Sale(new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc), 5),    // May → lifetime only
        };

        var summary = LedgerStats.Summarize(sales, PlusTwo, nowUtc);

        Assert.Equal(100, summary.NetToday);
        Assert.Equal(140, summary.NetThisWeek);
        Assert.Equal(150, summary.NetThisMonth);
        Assert.Equal(155, summary.TotalNetGil);
    }

    [Fact]
    public void Summarize_BestDay_PicksHighestNetEarliestOnTie() {
        var nowUtc = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        var sales = new[] {
            Sale(new DateTime(2026, 6, 9, 1, 0, 0, DateTimeKind.Utc), 70),
            Sale(new DateTime(2026, 6, 10, 1, 0, 0, DateTimeKind.Utc), 70),
            Sale(new DateTime(2026, 6, 11, 1, 0, 0, DateTimeKind.Utc), 30),
        };

        var summary = LedgerStats.Summarize(sales, TimeZoneInfo.Utc, nowUtc);

        Assert.Equal(new PeriodTotal(new DateOnly(2026, 6, 9), 70), summary.BestDay);
    }

    [Fact]
    public void Summarize_Averages_UseIntegerDivisionOverActiveDaysAndSales() {
        var nowUtc = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        var sales = new[] {
            Sale(new DateTime(2026, 6, 9, 1, 0, 0, DateTimeKind.Utc), 60),
            Sale(new DateTime(2026, 6, 9, 2, 0, 0, DateTimeKind.Utc), 25),
            Sale(new DateTime(2026, 6, 10, 1, 0, 0, DateTimeKind.Utc), 15),
        };

        var summary = LedgerStats.Summarize(sales, TimeZoneInfo.Utc, nowUtc);

        Assert.Equal(50, summary.AverageNetPerActiveDay); // 100 / 2 active days
        Assert.Equal(33, summary.AverageNetPerSale);      // 100 / 3 sales, floored
    }

    [Fact]
    public void Summarize_SplitsHqAndNq() {
        var nowUtc = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        var sales = new[] {
            Sale(new DateTime(2026, 6, 9, 1, 0, 0, DateTimeKind.Utc), 70, isHq: true, quantity: 2),
            Sale(new DateTime(2026, 6, 10, 1, 0, 0, DateTimeKind.Utc), 30, quantity: 3),
        };

        var summary = LedgerStats.Summarize(sales, TimeZoneInfo.Utc, nowUtc);

        Assert.Equal(70, summary.HqNetGil);
        Assert.Equal(1, summary.HqSaleCount);
        Assert.Equal(30, summary.NqNetGil);
        Assert.Equal(1, summary.NqSaleCount);
        Assert.Equal(5, summary.UnitsSold);
        Assert.Equal(2, summary.SaleCount);
    }

    [Fact]
    public void Summarize_LastSale_IsMaxSoldAt() {
        var nowUtc = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        var last = new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);
        var sales = new[] { Sale(new DateTime(2026, 6, 9, 1, 0, 0, DateTimeKind.Utc), 70), Sale(last, 30) };

        Assert.Equal(last, LedgerStats.Summarize(sales, TimeZoneInfo.Utc, nowUtc).LastSaleAtUtc);
    }
}
