using System;
using System.Collections.Generic;
using AscendedLedger;
using Xunit;

public class LedgerStatsTests {
    private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("p2", TimeSpan.FromHours(2), "p2", "p2");

    private static SaleRecord Sale(DateTime soldAtUtc, long net) => new() {
        OwnerContentId = 1,
        RetainerId = 1,
        ItemId = 100,
        Quantity = 1,
        UnitPrice = net,
        IsHq = false,
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
}
