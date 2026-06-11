using System;
using System.Collections.Generic;

using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AscendedLedger.Services;

/// <summary>
/// Captures the active retainer's market listings and held gil from game
/// memory whenever the retainer sell list loads or refreshes. Emits validated
/// Core values only; never mutates ledger state itself.
/// </summary>
internal sealed unsafe class RetainerMarketCaptureService : IDisposable {
    private const string RetainerSellListAddonName = "RetainerSellList";

    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;

    internal RetainerMarketCaptureService(IAddonLifecycle addonLifecycle, IPlayerState playerState, IPluginLog log) {
        this.addonLifecycle = addonLifecycle;
        this.playerState = playerState;
        this.log = log;
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, RetainerSellListAddonName, OnRetainerSellList);
        addonLifecycle.RegisterListener(AddonEvent.PostRefresh, RetainerSellListAddonName, OnRetainerSellList);
    }

    /// <summary>Raised with the retainer identity and a full validated snapshot.</summary>
    public event Action<Retainer, ListingSnapshot>? SnapshotCaptured;

    /// <inheritdoc/>
    public void Dispose() {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, RetainerSellListAddonName, OnRetainerSellList);
        addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, RetainerSellListAddonName, OnRetainerSellList);
    }

    private void OnRetainerSellList(AddonEvent type, AddonArgs args) {
        try {
            Capture();
        } catch (Exception exception) {
            log.Error(exception, "Retainer market capture failed.");
        }
    }

    private void Capture() {
        if (!playerState.IsLoaded) {
            return;
        }

        var retainerManager = RetainerManager.Instance();
        var inventoryManager = InventoryManager.Instance();
        if (retainerManager == null || inventoryManager == null) {
            return;
        }

        var active = retainerManager->GetActiveRetainer();
        if (active == null || active->RetainerId == 0) {
            return;
        }

        var container = inventoryManager->GetInventoryContainer(InventoryType.RetainerMarket);
        if (container == null) {
            return;
        }

        var listings = new List<Listing>();
        for (var slot = 0; slot < container->Size && slot < LedgerSerializer.MaxListingsPerSnapshot; slot++) {
            var item = container->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0) {
                continue;
            }

            var unitPrice = (long)inventoryManager->GetRetainerMarketPrice((short)slot);
            if (unitPrice == 0) {
                // Prices stream in after the container; a partial snapshot would
                // fake "vanished" listings, so capture nothing and let PostRefresh retry.
                log.Debug("Retainer market prices not yet loaded; skipping capture.");
                return;
            }

            var quantity = item->Quantity;
            if (quantity < 1 || quantity > LedgerSerializer.MaxQuantity || unitPrice > LedgerSerializer.MaxUnitPrice) {
                log.Warning("Skipping retainer market slot {Slot}: quantity or price out of range.", slot);
                continue;
            }

            var isHq = item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);
            listings.Add(new Listing(slot, item->ItemId, quantity, unitPrice, isHq));
        }

        var townByte = (byte)active->Town;
        var town = Enum.IsDefined((Town)townByte) ? (Town)townByte : Town.Unknown;
        var retainer = new Retainer(active->RetainerId, playerState.ContentId, NameSanitizer.Sanitize(active->NameString), town);
        var snapshot = new ListingSnapshot(active->RetainerId, DateTime.UtcNow, (long)active->Gil, listings);
        log.Debug("Captured {Count} listing(s) for retainer {RetainerId} (gil {Gil}, town byte {TownByte}).", listings.Count, retainer.RetainerId, snapshot.RetainerGil, townByte);
        SnapshotCaptured?.Invoke(retainer, snapshot);
    }
}
