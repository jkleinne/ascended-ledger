using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using AscendedLedger.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AscendedLedger.Ui;

/// <summary>
/// Main plugin window: a character selector over three read-only views of the
/// ledger (current listings, completed sales, period stats). Renders state
/// owned by the coordinator; performs no domain logic beyond filtering and
/// column ordering.
/// </summary>
internal sealed class MainWindow : Window {
    private const string Title = "Ascended Ledger";
    private const float MinimumWidth = 480;
    private const float MinimumHeight = 320;
    private const ulong AllCharacters = 0;
    private const string EstimateMarker = "≈";
    private const string DetectedAtMarker = "~";
    private const int ListingsColumnCount = 6;
    private const int SalesColumnCount = 6;

    /// <summary>Local-time display format shared by the Listings as-of and Sales sold-at columns.</summary>
    private const string TimestampFormat = "yyyy-MM-dd HH:mm";

    private readonly LedgerCoordinator coordinator;
    private readonly ItemNameResolver itemNames;
    private readonly StatsTabView statsTab;

    private ulong selectedOwner = AllCharacters;

    internal MainWindow(LedgerCoordinator coordinator, ItemNameResolver itemNames) : base(Title) {
        this.coordinator = coordinator;
        this.itemNames = itemNames;
        statsTab = new StatsTabView(coordinator, itemNames);
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(MinimumWidth, MinimumHeight),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    /// <inheritdoc/>
    public override void Draw() {
        if (coordinator.IsHistoryCaptureDegraded) {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "Sale-history capture unavailable (game update?). Sales are still inferred from listing diffs.");
        }

        if (coordinator.RecoveryNotice is { } recoveryNotice) {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.TextUnformatted(recoveryNotice);
            ImGui.PopStyleColor();
        }

        DrawCharacterSelector();
        if (!ImGui.BeginTabBar("##ledgerTabs")) {
            return;
        }

        if (ImGui.BeginTabItem("Listings")) {
            DrawListingsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Sales")) {
            DrawSalesTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Stats")) {
            statsTab.Draw(selectedOwner, VisibleSales(), VisibleRetainers());
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawCharacterSelector() {
        var ledger = coordinator.Ledger;
        var label = selectedOwner == AllCharacters
            ? "All characters"
            : ledger.CharactersById.TryGetValue(selectedOwner, out var selected) ? selected.Name : "All characters";
        if (!ImGui.BeginCombo("Character", label)) {
            return;
        }

        if (ImGui.Selectable("All characters", selectedOwner == AllCharacters)) {
            selectedOwner = AllCharacters;
        }

        foreach (var character in ledger.CharactersById.Values.OrderBy(c => c.Name, StringComparer.Ordinal)) {
            if (ImGui.Selectable($"{character.Name} ({character.World})##{character.ContentId}", selectedOwner == character.ContentId)) {
                selectedOwner = character.ContentId;
            }
        }

        ImGui.EndCombo();
    }

    private IEnumerable<Retainer> VisibleRetainers() =>
        coordinator.Ledger.RetainersById.Values
            .Where(r => selectedOwner == AllCharacters || r.OwnerContentId == selectedOwner)
            .OrderBy(r => r.Name, StringComparer.Ordinal);

    private IEnumerable<SaleRecord> VisibleSales() =>
        coordinator.Ledger.Sales
            .Where(s => selectedOwner == AllCharacters || s.OwnerContentId == selectedOwner);

    private void DrawListingsTab() {
        if (!ImGui.BeginTable("##listings", ListingsColumnCount, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable)) {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.DefaultSort);
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Unit price");
        ImGui.TableSetupColumn("Expected net");
        ImGui.TableSetupColumn("As of");
        ImGui.TableHeadersRow();

        foreach (var row in SortedListingRows(BuildListingRows(), ImGui.TableGetSortSpecs())) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Retainer);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Item);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Quantity.ToString(CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiFormat.Gil(row.UnitPrice));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiFormat.Gil(row.ExpectedNet));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.AsOfLocal);
        }

        ImGui.EndTable();
    }

    private List<ListingRow> BuildListingRows() {
        var rows = new List<ListingRow>();
        var ledger = coordinator.Ledger;
        foreach (var retainer in VisibleRetainers()) {
            if (!ledger.LatestSnapshotsByRetainerId.TryGetValue(retainer.RetainerId, out var snapshot)) {
                continue;
            }

            var ratePercent = coordinator.RatePercentFor(retainer.Town);
            var asOfLocal = snapshot.ObservedAtUtc.ToLocalTime().ToString(TimestampFormat, CultureInfo.CurrentCulture);
            foreach (var listing in snapshot.Listings) {
                rows.Add(new ListingRow(
                    retainer.Name,
                    itemNames.NameOf(listing.ItemId) + (listing.IsHq ? " (HQ)" : string.Empty),
                    listing.Quantity,
                    listing.UnitPrice,
                    ProceedsCalculator.Net(ProceedsCalculator.Gross(listing.Quantity, listing.UnitPrice), ratePercent),
                    snapshot.ObservedAtUtc,
                    asOfLocal));
            }
        }

        return rows;
    }

    private static List<ListingRow> SortedListingRows(List<ListingRow> rows, ImGuiTableSortSpecsPtr sortSpecs) {
        if (sortSpecs.IsNull || sortSpecs.SpecsCount == 0) {
            return rows;
        }

        // Single-column sort: SortMulti is not enabled, so the first spec is the only one.
        var spec = sortSpecs.Specs;
        var descending = spec.SortDirection == ImGuiSortDirection.Descending;
        return (ListingsColumn)spec.ColumnIndex switch {
            ListingsColumn.Retainer => OrderRows(rows, r => r.Retainer, descending, StringComparer.Ordinal),
            ListingsColumn.Item => OrderRows(rows, r => r.Item, descending, StringComparer.Ordinal),
            ListingsColumn.Quantity => OrderRows(rows, r => r.Quantity, descending),
            ListingsColumn.UnitPrice => OrderRows(rows, r => r.UnitPrice, descending),
            ListingsColumn.ExpectedNet => OrderRows(rows, r => r.ExpectedNet, descending),
            ListingsColumn.AsOf => OrderRows(rows, r => r.ObservedAtUtc, descending),
            _ => rows,
        };
    }

    private void DrawSalesTab() {
        if (!ImGui.BeginTable("##sales", SalesColumnCount, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable)) {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Sold at", ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending);
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Net");
        ImGui.TableSetupColumn("Retainer");
        ImGui.TableSetupColumn("Buyer");
        ImGui.TableHeadersRow();

        foreach (var row in SortedSaleRows(BuildSaleRows(), ImGui.TableGetSortSpecs())) {
            var soldAt = row.SoldAtUtc.ToLocalTime().ToString(TimestampFormat, CultureInfo.CurrentCulture);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.SoldAtPrecision == SoldAtPrecision.DetectedAt ? DetectedAtMarker + soldAt : soldAt);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Item);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Quantity.ToString(CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((row.IsTaxEstimated ? EstimateMarker : string.Empty) + UiFormat.Gil(row.NetGil));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Retainer);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Buyer);
        }

        ImGui.EndTable();
    }

    private List<SaleRow> BuildSaleRows() {
        var rows = new List<SaleRow>();
        var ledger = coordinator.Ledger;
        foreach (var sale in VisibleSales()) {
            var retainerName = ledger.RetainersById.TryGetValue(sale.RetainerId, out var retainer)
                ? retainer.Name
                : sale.RetainerId.ToString(CultureInfo.InvariantCulture);
            rows.Add(new SaleRow(
                sale.SoldAtUtc,
                sale.SoldAtPrecision,
                itemNames.NameOf(sale.ItemId) + (sale.IsHq ? " (HQ)" : string.Empty),
                sale.Quantity,
                sale.NetGil,
                sale.IsTaxEstimated,
                retainerName,
                sale.BuyerName ?? string.Empty));
        }

        return rows;
    }

    private static List<SaleRow> SortedSaleRows(List<SaleRow> rows, ImGuiTableSortSpecsPtr sortSpecs) {
        if (sortSpecs.IsNull || sortSpecs.SpecsCount == 0) {
            return rows;
        }

        // Single-column sort: SortMulti is not enabled, so the first spec is the only one.
        var spec = sortSpecs.Specs;
        var descending = spec.SortDirection == ImGuiSortDirection.Descending;
        return (SalesColumn)spec.ColumnIndex switch {
            SalesColumn.SoldAt => OrderRows(rows, r => r.SoldAtUtc, descending),
            SalesColumn.Item => OrderRows(rows, r => r.Item, descending, StringComparer.Ordinal),
            SalesColumn.Quantity => OrderRows(rows, r => r.Quantity, descending),
            SalesColumn.Net => OrderRows(rows, r => r.NetGil, descending),
            SalesColumn.Retainer => OrderRows(rows, r => r.Retainer, descending, StringComparer.Ordinal),
            SalesColumn.Buyer => OrderRows(rows, r => r.Buyer, descending, StringComparer.Ordinal),
            _ => rows,
        };
    }

    /// <summary>
    /// Stable ordering shared by all sortable tables. LINQ OrderBy keeps ties in
    /// projection order, which preserves today's within-retainer listing order
    /// under the default sort.
    /// </summary>
    private static List<TRow> OrderRows<TRow, TKey>(List<TRow> rows, Func<TRow, TKey> key, bool descending, IComparer<TKey>? comparer = null) =>
        (descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer)).ToList();

    /// <summary>Column ordinals for the Listings table; must match the TableSetupColumn order in DrawListingsTab.</summary>
    private enum ListingsColumn {
        Retainer = 0,
        Item = 1,
        Quantity = 2,
        UnitPrice = 3,
        ExpectedNet = 4,
        AsOf = 5,
    }

    /// <summary>
    /// One Listings row, flattened so any column can order the whole table.
    /// Carries the raw observed-at instant for sorting and the preformatted
    /// local string for rendering. A struct because rows are rebuilt every
    /// frame and per-row heap allocations would be pure GC churn.
    /// </summary>
    private readonly record struct ListingRow(
        string Retainer,
        string Item,
        int Quantity,
        long UnitPrice,
        long ExpectedNet,
        DateTime ObservedAtUtc,
        string AsOfLocal);

    /// <summary>Column ordinals for the Sales table; must match the TableSetupColumn order in DrawSalesTab.</summary>
    private enum SalesColumn {
        SoldAt = 0,
        Item = 1,
        Quantity = 2,
        Net = 3,
        Retainer = 4,
        Buyer = 5,
    }

    /// <summary>
    /// One Sales row, flattened with names pre-resolved so comparers are plain
    /// field reads. Sort keys are the raw values; the ~ and ≈ markers are applied
    /// from the precision/estimate fields at render time so they never perturb
    /// ordering. A struct for the same per-frame-rebuild reason as ListingRow.
    /// </summary>
    private readonly record struct SaleRow(
        DateTime SoldAtUtc,
        SoldAtPrecision SoldAtPrecision,
        string Item,
        int Quantity,
        long NetGil,
        bool IsTaxEstimated,
        string Retainer,
        string Buyer);
}
