using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using AscendedLedger.Services;
using Dalamud.Bindings.ImGui;

namespace AscendedLedger.Ui;

/// <summary>
/// Renders the Stats tab: earnings KPIs, current-listings overview, top
/// breakdowns, and the per-period net table. All aggregation happens in
/// Core; this view assembles inputs, caches the computed summaries keyed on
/// ledger revision, and draws them.
/// </summary>
internal sealed class StatsTabView {
    private const int TopCount = 10;
    private const string HqSuffix = " (HQ)";
    private const string NoValue = "—";
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    private static readonly string[] PeriodLabels = ["Day", "Week", "Month"];
    private static readonly Vector4 MutedLabelColor = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.6f, 0.2f, 1f);
    private static readonly TimeSpan StaleSnapshotWarningAge = TimeSpan.FromHours(24);

    private readonly LedgerCoordinator coordinator;
    private readonly ItemNameResolver itemNames;

    private int selectedPeriodIndex;
    private CacheKey? cachedKey;
    private SalesSummary salesSummary = SalesSummary.Empty;
    private ListingsSummary listingsSummary = ListingsSummary.Empty;
    private IReadOnlyList<ItemBreakdown> topItems = Array.Empty<ItemBreakdown>();
    private IReadOnlyList<RetainerBreakdown> topRetainers = Array.Empty<RetainerBreakdown>();
    private IReadOnlyList<BuyerBreakdown> topBuyers = Array.Empty<BuyerBreakdown>();
    private IReadOnlyList<PeriodTotal> periodTotals = Array.Empty<PeriodTotal>();

    internal StatsTabView(LedgerCoordinator coordinator, ItemNameResolver itemNames) {
        this.coordinator = coordinator;
        this.itemNames = itemNames;
    }

    /// <summary>One computed stats state; any component change forces a recompute.</summary>
    private readonly record struct CacheKey(long Revision, ulong Owner, int PeriodIndex, DateTime LocalHour);

    /// <summary>
    /// Draws the tab. The enumerables are lazy owner-filtered views supplied
    /// by <see cref="MainWindow"/> (which owns the character selector); they
    /// are enumerated only when the cache key changes.
    /// </summary>
    public void Draw(ulong selectedOwner, IEnumerable<SaleRecord> visibleSales, IEnumerable<Retainer> visibleRetainers) {
        ImGui.Combo("Period", ref selectedPeriodIndex, PeriodLabels, PeriodLabels.Length);
        RefreshIfStale(selectedOwner, visibleSales, visibleRetainers);

        if (salesSummary.SaleCount == 0 && listingsSummary.Rows.Count == 0) {
            ImGui.TextUnformatted("No sales recorded yet. Visit a retainer to start capturing.");
            return;
        }

        DrawKpis();
        DrawListingsSection();
        DrawBreakdownsSection();
        DrawPeriodSection();
    }

    private void RefreshIfStale(ulong selectedOwner, IEnumerable<SaleRecord> visibleSales, IEnumerable<Retainer> visibleRetainers) {
        // One clock read feeds both the cache key and the computation so the
        // key's hour bucket can never disagree with the data it labels.
        var nowUtc = DateTime.UtcNow;
        var nowLocal = nowUtc.ToLocalTime();
        var localHour = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, 0, 0, DateTimeKind.Local);
        var key = new CacheKey(coordinator.Ledger.Revision, selectedOwner, selectedPeriodIndex, localHour);
        if (cachedKey == key) {
            return;
        }

        cachedKey = key;
        Recompute(visibleSales, visibleRetainers, nowUtc);
    }

    private void Recompute(IEnumerable<SaleRecord> visibleSales, IEnumerable<Retainer> visibleRetainers, DateTime nowUtc) {
        var sales = visibleSales.ToList();
        salesSummary = LedgerStats.Summarize(sales, TimeZoneInfo.Local, nowUtc);
        topItems = LedgerStats.TopItems(sales, TopCount);
        topRetainers = LedgerStats.TopRetainers(sales, TopCount);
        topBuyers = LedgerStats.TopBuyers(sales, TopCount);
        periodTotals = LedgerStats.NetByPeriod(sales, TimeZoneInfo.Local, (StatsPeriod)selectedPeriodIndex);

        var snapshots = coordinator.Ledger.LatestSnapshotsByRetainerId;
        var inputs = new List<ListingStatsInput>();
        foreach (var retainer in visibleRetainers) {
            if (snapshots.TryGetValue(retainer.RetainerId, out var snapshot)) {
                inputs.Add(new ListingStatsInput(snapshot, coordinator.RatePercentFor(retainer.Town)));
            }
        }

        listingsSummary = ListingStats.Summarize(inputs, nowUtc);
    }

    private void DrawKpis() {
        if (!ImGui.BeginTable("##statsKpis", 4)) {
            return;
        }

        DrawKpiCell("Today", UiFormat.Gil(salesSummary.NetToday));
        DrawKpiCell("This week", UiFormat.Gil(salesSummary.NetThisWeek));
        DrawKpiCell("This month", UiFormat.Gil(salesSummary.NetThisMonth));
        DrawKpiCell("Lifetime net", UiFormat.Gil(salesSummary.TotalNetGil));
        DrawKpiCell("Lifetime gross", UiFormat.Gil(salesSummary.TotalGrossGil));
        DrawKpiCell("Tax paid", UiFormat.Gil(salesSummary.TotalTaxGil));
        DrawKpiCell("Sales", UiFormat.Count(salesSummary.SaleCount));
        DrawKpiCell("Units sold", UiFormat.Count(salesSummary.UnitsSold));
        DrawKpiCell("Avg / sale", UiFormat.Gil(salesSummary.AverageNetPerSale));
        DrawKpiCell("Avg / active day", UiFormat.Gil(salesSummary.AverageNetPerActiveDay));
        DrawKpiCell("Best day", salesSummary.BestDay is { } best
            ? $"{best.PeriodStart.ToString(DateFormat, CultureInfo.CurrentCulture)} ({UiFormat.Gil(best.NetGil)})"
            : NoValue);
        DrawKpiCell("Last sale", salesSummary.LastSaleAtUtc is { } last
            ? last.ToLocalTime().ToString(DateTimeFormat, CultureInfo.CurrentCulture)
            : NoValue);
        ImGui.EndTable();
    }

    private static void DrawKpiCell(string label, string value) {
        ImGui.TableNextColumn();
        ImGui.TextColored(MutedLabelColor, label);
        ImGui.TextUnformatted(value);
    }

    private void DrawListingsSection() {
        if (!ImGui.CollapsingHeader("Current listings", ImGuiTreeNodeFlags.DefaultOpen)) {
            return;
        }

        if (listingsSummary.Rows.Count == 0) {
            ImGui.TextUnformatted("No listing snapshots yet. Visit a retainer to start capturing.");
            return;
        }

        var summary = listingsSummary;
        var nowUtc = DateTime.UtcNow;
        ImGui.TextUnformatted($"{summary.ListingCount} listings ({UiFormat.Count(summary.TotalUnits)} units) across {summary.Rows.Count} retainers — slots {summary.SlotsUsed}/{summary.SlotsTotal}");
        ImGui.TextUnformatted($"Expected gain: {UiFormat.Gil(summary.ExpectedGrossGil)} gross → {UiFormat.Gil(summary.ExpectedNetGil)} net");
        ImGui.TextUnformatted($"Gil held on retainers: {UiFormat.Gil(summary.TotalRetainerGil)}");
        if (summary.Oldest is { } oldest) {
            var itemLabel = itemNames.NameOf(oldest.ItemId) + (oldest.IsHq ? HqSuffix : string.Empty);
            ImGui.TextUnformatted($"Oldest listing: {itemLabel} on {RetainerName(oldest.RetainerId)} — {UiFormat.Age(nowUtc - oldest.FirstSeenUtc)}");
            ImGui.TextUnformatted($"Average listing age: {UiFormat.Age(summary.AverageListingAge)}");
        }

        if (summary.StalestSnapshotUtc is { } stalest && nowUtc - stalest > StaleSnapshotWarningAge) {
            ImGui.TextColored(WarningColor, $"Stalest retainer data is {UiFormat.Age(nowUtc - stalest)} old; visit retainers to refresh.");
        }

        DrawRetainerRows(nowUtc);
    }

    private void DrawRetainerRows(DateTime nowUtc) {
        if (!ImGui.BeginTable("##statsRetainers", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable)) {
            return;
        }

        ImGui.TableSetupColumn("Retainer");
        ImGui.TableSetupColumn("Listings");
        ImGui.TableSetupColumn("Units");
        ImGui.TableSetupColumn("Expected net");
        ImGui.TableSetupColumn("Gil held");
        ImGui.TableSetupColumn("Last visited");
        ImGui.TableSetupColumn("Oldest age");
        ImGui.TableHeadersRow();

        foreach (var row in listingsSummary.Rows) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(RetainerName(row.RetainerId));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.ListingCount.ToString(CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Units.ToString(CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiFormat.Gil(row.ExpectedNetGil));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiFormat.Gil(row.RetainerGil));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.ObservedAtUtc.ToLocalTime().ToString(DateTimeFormat, CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.OldestFirstSeenUtc is { } oldestSeen ? UiFormat.Age(nowUtc - oldestSeen) : NoValue);
        }

        ImGui.EndTable();
    }

    private void DrawBreakdownsSection() {
        if (!ImGui.CollapsingHeader("Top breakdowns", ImGuiTreeNodeFlags.DefaultOpen)) {
            return;
        }

        if (salesSummary.SaleCount == 0) {
            ImGui.TextUnformatted("No sales recorded yet.");
            return;
        }

        ImGui.TextUnformatted(
            $"HQ: {UiFormat.Gil(salesSummary.HqNetGil)} over {salesSummary.HqSaleCount} sales — " +
            $"NQ: {UiFormat.Gil(salesSummary.NqNetGil)} over {salesSummary.NqSaleCount} sales");

        DrawTopItems();
        DrawTopRetainers();
        if (topBuyers.Count > 0) {
            DrawTopBuyers();
        }
    }

    private void DrawTopItems() {
        ImGui.TextColored(MutedLabelColor, "Top items");
        if (!ImGui.BeginTable("##statsTopItems", 4, ImGuiTableFlags.RowBg)) {
            return;
        }

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Net");
        ImGui.TableSetupColumn("Sales");
        ImGui.TableSetupColumn("Units");
        ImGui.TableHeadersRow();
        foreach (var item in topItems) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(itemNames.NameOf(item.ItemId) + (item.IsHq ? HqSuffix : string.Empty));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiFormat.Gil(item.NetGil));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.SaleCount.ToString(CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.Units.ToString(CultureInfo.CurrentCulture));
        }

        ImGui.EndTable();
    }

    private void DrawTopRetainers() {
        ImGui.TextColored(MutedLabelColor, "Top retainers");
        if (!ImGui.BeginTable("##statsTopRetainers", 3, ImGuiTableFlags.RowBg)) {
            return;
        }

        ImGui.TableSetupColumn("Retainer");
        ImGui.TableSetupColumn("Net");
        ImGui.TableSetupColumn("Sales");
        ImGui.TableHeadersRow();
        foreach (var retainer in topRetainers) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(RetainerName(retainer.RetainerId));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiFormat.Gil(retainer.NetGil));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(retainer.SaleCount.ToString(CultureInfo.CurrentCulture));
        }

        ImGui.EndTable();
    }

    private void DrawTopBuyers() {
        ImGui.TextColored(MutedLabelColor, "Top buyers");
        if (!ImGui.BeginTable("##statsTopBuyers", 3, ImGuiTableFlags.RowBg)) {
            return;
        }

        ImGui.TableSetupColumn("Buyer");
        ImGui.TableSetupColumn("Net");
        ImGui.TableSetupColumn("Sales");
        ImGui.TableHeadersRow();
        foreach (var buyer in topBuyers) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(buyer.BuyerName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiFormat.Gil(buyer.NetGil));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(buyer.SaleCount.ToString(CultureInfo.CurrentCulture));
        }

        ImGui.EndTable();
    }

    private void DrawPeriodSection() {
        if (!ImGui.CollapsingHeader("Net by period", ImGuiTreeNodeFlags.DefaultOpen)) {
            return;
        }

        if (periodTotals.Count == 0) {
            ImGui.TextUnformatted("No sales recorded yet.");
            return;
        }

        if (!ImGui.BeginTable("##statsPeriods", 2, ImGuiTableFlags.RowBg)) {
            return;
        }

        ImGui.TableSetupColumn("Period starting");
        ImGui.TableSetupColumn("Net gil");
        ImGui.TableHeadersRow();
        foreach (var total in periodTotals) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(total.PeriodStart.ToString(DateFormat, CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiFormat.Gil(total.NetGil));
        }

        ImGui.EndTable();
    }

    private string RetainerName(ulong retainerId) =>
        coordinator.Ledger.RetainersById.TryGetValue(retainerId, out var retainer)
            ? retainer.Name
            : retainerId.ToString(CultureInfo.InvariantCulture);
}
