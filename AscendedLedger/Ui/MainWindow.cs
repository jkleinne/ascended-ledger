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
/// owned by the coordinator; performs no domain logic beyond filtering.
/// </summary>
internal sealed class MainWindow : Window {
    private const string Title = "Ascended Ledger";
    private const float MinimumWidth = 480;
    private const float MinimumHeight = 320;
    private const ulong AllCharacters = 0;
    private const string EstimateMarker = "≈";
    private const string DetectedAtMarker = "~";

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
        if (!ImGui.BeginTable("##listings", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY)) {
            return;
        }

        ImGui.TableSetupColumn("Retainer");
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Unit price");
        ImGui.TableSetupColumn("Expected net");
        ImGui.TableSetupColumn("As of");
        ImGui.TableHeadersRow();

        var ledger = coordinator.Ledger;
        foreach (var retainer in VisibleRetainers()) {
            if (!ledger.LatestSnapshotsByRetainerId.TryGetValue(retainer.RetainerId, out var snapshot)) {
                continue;
            }

            var ratePercent = coordinator.RatePercentFor(retainer.Town);
            var asOfLocal = snapshot.ObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            foreach (var listing in snapshot.Listings) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(retainer.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(itemNames.NameOf(listing.ItemId) + (listing.IsHq ? " (HQ)" : string.Empty));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(listing.Quantity.ToString(CultureInfo.CurrentCulture));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(UiFormat.Gil(listing.UnitPrice));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(UiFormat.Gil(ProceedsCalculator.Net(ProceedsCalculator.Gross(listing.Quantity, listing.UnitPrice), ratePercent)));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(asOfLocal);
            }
        }

        ImGui.EndTable();
    }

    private void DrawSalesTab() {
        if (!ImGui.BeginTable("##sales", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY)) {
            return;
        }

        ImGui.TableSetupColumn("Sold at");
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Net");
        ImGui.TableSetupColumn("Retainer");
        ImGui.TableSetupColumn("Buyer");
        ImGui.TableHeadersRow();

        var ledger = coordinator.Ledger;
        foreach (var sale in VisibleSales().OrderByDescending(s => s.SoldAtUtc)) {
            var retainerName = ledger.RetainersById.TryGetValue(sale.RetainerId, out var retainer)
                ? retainer.Name
                : sale.RetainerId.ToString(CultureInfo.InvariantCulture);
            var soldAt = sale.SoldAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sale.SoldAtPrecision == SoldAtPrecision.DetectedAt ? DetectedAtMarker + soldAt : soldAt);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(itemNames.NameOf(sale.ItemId) + (sale.IsHq ? " (HQ)" : string.Empty));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sale.Quantity.ToString(CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((sale.IsTaxEstimated ? EstimateMarker : string.Empty) + UiFormat.Gil(sale.NetGil));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(retainerName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sale.BuyerName ?? string.Empty);
        }

        ImGui.EndTable();
    }
}
