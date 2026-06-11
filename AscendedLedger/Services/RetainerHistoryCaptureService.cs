using System;
using System.Collections.Generic;

using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AscendedLedger.Services;

/// <summary>
/// Captures ground-truth sale history (real timestamps, buyers) by hooking the
/// game's history processing, which runs when the player opens a retainer's
/// sale-history list. A signature miss after a game patch degrades to
/// diff-only capture instead of failing the plugin.
/// </summary>
internal sealed unsafe class RetainerHistoryCaptureService : IDisposable {
    private const string ProcessRetainerHistorySignature = "40 53 56 57 41 57 48 83 EC 38 48 8B F1";
    private const int MaxHistoryEntries = 20;
    private const int EntriesOffset = 8;

    private readonly IPluginLog log;
    private readonly Hook<ProcessRetainerHistoryDelegate>? hook;

    internal RetainerHistoryCaptureService(IGameInteropProvider gameInterop, IPluginLog log) {
        this.log = log;
        try {
            hook = gameInterop.HookFromSignature<ProcessRetainerHistoryDelegate>(ProcessRetainerHistorySignature, OnProcessRetainerHistory);
            hook.Enable();
        } catch (Exception exception) {
            log.Warning(exception, "ProcessRetainerHistory signature not found; sale-history capture degraded to diff-only.");
            IsDegraded = true;
        }
    }

    private delegate nint ProcessRetainerHistoryDelegate(nint agent, nint packetData);

    /// <summary>Raised with the active retainer id and the parsed history entries.</summary>
    public event Action<ulong, IReadOnlyList<HistorySale>>? HistoryCaptured;

    /// <summary>True when the hook could not be installed (sale-history capture inactive).</summary>
    public bool IsDegraded { get; }

    /// <inheritdoc/>
    public void Dispose() => hook?.Dispose();

    private nint OnProcessRetainerHistory(nint agent, nint packetData) {
        var result = hook!.Original(agent, packetData);
        try {
            CaptureEntries(packetData);
        } catch (Exception exception) {
            log.Error(exception, "Failed to parse retainer sale history.");
        }

        return result;
    }

    private void CaptureEntries(nint packetData) {
        var retainerManager = RetainerManager.Instance();
        var active = retainerManager == null ? null : retainerManager->GetActiveRetainer();
        if (active == null || active->RetainerId == 0) {
            log.Warning("Sale history received with no active retainer; cannot attribute, skipping.");
            return;
        }

        var entries = new List<HistorySale>(MaxHistoryEntries);
        for (var index = 0; index < MaxHistoryEntries; index++) {
            var entry = (RetainerHistoryEntry*)(packetData + EntriesOffset + (index * RetainerHistoryEntry.Size));
            if (entry->ItemId == 0) {
                break;
            }

            if (entry->IsMannequin) {
                continue;
            }

            var quantity = (int)entry->Quantity;
            var grossGil = (long)entry->Price;
            var exceedsPriceCap = grossGil > LedgerSerializer.MaxUnitPrice * quantity;
            if (quantity < 1 || quantity > LedgerSerializer.MaxQuantity || grossGil < 1 || exceedsPriceCap) {
                // A single garbage entry persisted past the unit-price cap would make
                // the whole file a structural violation on next load; reject it here.
                log.Warning("Skipping malformed sale-history entry {Index}.", index);
                continue;
            }

            var soldAtUtc = DateTimeOffset.FromUnixTimeSeconds(entry->UnixTimeSeconds).UtcDateTime;
            entries.Add(new HistorySale(entry->ItemId, quantity, grossGil, entry->IsHq, soldAtUtc, NameSanitizer.Sanitize(entry->BuyerName)));
        }

        if (entries.Count > 0) {
            log.Debug("Captured {Count} sale-history entr(ies) for retainer {RetainerId}.", entries.Count, active->RetainerId);
            HistoryCaptured?.Invoke(active->RetainerId, entries);
        }
    }
}
