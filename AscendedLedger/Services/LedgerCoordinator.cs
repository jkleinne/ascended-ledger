using System;
using System.Collections.Generic;

using AscendedLedger.Persistence;

using Dalamud.Plugin.Services;

namespace AscendedLedger.Services;

/// <summary>
/// The single mutation owner for all ledger state. Routes capture events from
/// <see cref="RetainerMarketCaptureService"/> and
/// <see cref="RetainerHistoryCaptureService"/> through the <see cref="Ledger"/>
/// aggregate, debounces atomic saves via a 2-second dirty window, surfaces
/// recovery and repeated-save-failure notices to chat, and owns the tax-rate
/// fallback chain (live rates → persisted rates → default).
///
/// <para>
/// All event handlers are invoked on Dalamud's framework thread; no locking is
/// required because mutations never cross thread boundaries.
/// </para>
/// </summary>
internal sealed class LedgerCoordinator : IDisposable {
    private const int SaveDebounceMilliseconds = 2000;
    private const int SaveFailureNoticeThreshold = 3;
    private const string ChatPrefix = "[Ascended Ledger] ";

    private readonly ILedgerStore store;
    private readonly TaxRateService taxRates;
    private readonly RetainerMarketCaptureService marketCapture;
    private readonly RetainerHistoryCaptureService historyCapture;
    private readonly IPlayerState playerState;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly IChatGui chat;

    /// <summary>Tick count at which the ledger first became dirty; 0 means clean.</summary>
    private long dirtySinceTick;

    /// <summary>Consecutive save failures since the last successful save.</summary>
    private int consecutiveSaveFailures;

    /// <summary>True once the save-failure chat notice has been printed for this failure run.</summary>
    private bool saveFailureNotified;

    /// <summary>
    /// Loads the persisted ledger and subscribes to all capture and framework
    /// events. If the file was unusable, prints a one-time recovery notice to
    /// chat and exposes it via <see cref="RecoveryNotice"/> for the UI.
    /// </summary>
    internal LedgerCoordinator(
        ILedgerStore store,
        TaxRateService taxRates,
        RetainerMarketCaptureService marketCapture,
        RetainerHistoryCaptureService historyCapture,
        IPlayerState playerState,
        IFramework framework,
        IPluginLog log,
        IChatGui chat) {
        this.store = store;
        this.taxRates = taxRates;
        this.marketCapture = marketCapture;
        this.historyCapture = historyCapture;
        this.playerState = playerState;
        this.framework = framework;
        this.log = log;
        this.chat = chat;

        var outcome = store.Load();
        Ledger = outcome.Ledger;

        if (outcome.RecoveredFromError != LedgerLoadError.None) {
            RecoveryNotice = $"Ledger recovered from {outcome.RecoveredFromError}. Unusable file backed up to: {outcome.BackupPath}";
            chat.PrintError(ChatPrefix + RecoveryNotice);
        }

        marketCapture.SnapshotCaptured += OnSnapshotCaptured;
        historyCapture.HistoryCaptured += OnHistoryCaptured;
        taxRates.RatesUpdated += OnRatesUpdated;
        framework.Update += OnFrameworkUpdate;
    }

    /// <summary>The live ledger aggregate. Read by the UI; mutated only by this coordinator.</summary>
    public Ledger Ledger { get; }

    /// <summary>
    /// Set when the ledger file was unusable at startup and a recovery was
    /// performed. Null when the file loaded cleanly or did not exist yet.
    /// Exposed for the UI to surface alongside the chat notice.
    /// </summary>
    public string? RecoveryNotice { get; }

    /// <summary>
    /// True when the history-capture hook could not be installed after a game
    /// patch. Re-exposed from <see cref="RetainerHistoryCaptureService.IsDegraded"/>
    /// for the UI warning banner.
    /// </summary>
    public bool IsHistoryCaptureDegraded => historyCapture.IsDegraded;

    /// <summary>
    /// Returns the market tax rate for <paramref name="town"/> using the
    /// best available source: live rates from the game, then the persisted
    /// snapshot stored in the ledger, then the hard-coded default (5 %).
    /// <see cref="MarketTaxRatesSnapshot.RateFor"/> already handles unknown
    /// towns by returning the default, so missing towns are harmless — all
    /// eight towns are captured by Task 8 whenever a rate packet arrives.
    /// </summary>
    public int RatePercentFor(Town town) =>
        taxRates.Current?.RateFor(town) ?? Ledger.TaxRates?.RateFor(town) ?? ProceedsCalculator.DefaultTaxRatePercent;

    /// <inheritdoc/>
    public void Dispose() {
        // Unsubscribe in reverse subscription order.
        framework.Update -= OnFrameworkUpdate;
        taxRates.RatesUpdated -= OnRatesUpdated;
        historyCapture.HistoryCaptured -= OnHistoryCaptured;
        marketCapture.SnapshotCaptured -= OnSnapshotCaptured;

        // Final flush. A crash here would take Dalamud's UI thread down, so
        // we eat the exception. Data loss is bounded by the debounce window
        // (at most 2 s of captures that arrived after the last successful save).
        if (dirtySinceTick > 0) {
            try {
                store.Save(Ledger);
            } catch (Exception exception) {
                log.Error(exception, "Final ledger save on unload failed; changes since the last save are lost.");
            }
        }
    }

    private void OnSnapshotCaptured(Retainer retainer, ListingSnapshot snapshot) {
        UpsertCurrentCharacter();
        Ledger.UpsertRetainer(retainer);
        var inferred = Ledger.ApplySnapshot(snapshot, RatePercentFor(retainer.Town), retainer.OwnerContentId);
        if (inferred.Count > 0) {
            log.Information("Inferred {Count} sale(s) from retainer {RetainerId} snapshot.", inferred.Count, retainer.RetainerId);
        }

        MarkDirty();
    }

    private void OnHistoryCaptured(ulong retainerId, IReadOnlyList<HistorySale> entries) {
        UpsertCurrentCharacter();
        Ledger.RetainersById.TryGetValue(retainerId, out var retainer);
        var town = retainer?.Town ?? Town.Unknown;
        var changed = Ledger.ApplyHistory(retainerId, playerState.ContentId, entries, RatePercentFor(town));
        if (changed > 0) {
            log.Information("Merged {Count} sale record(s) from history for retainer {RetainerId}.", changed, retainerId);
            MarkDirty();
        }
    }

    private void OnRatesUpdated() {
        if (taxRates.Current is { } current) {
            Ledger.SetTaxRates(current);
            MarkDirty();
        }
    }

    private void OnFrameworkUpdate(IFramework fw) {
        if (dirtySinceTick > 0 && Environment.TickCount64 - dirtySinceTick >= SaveDebounceMilliseconds) {
            TrySave();
        }
    }

    /// <summary>
    /// Upserts the currently logged-in character. No-ops when the player state
    /// is not yet loaded (e.g. at the title screen or between areas).
    /// </summary>
    private void UpsertCurrentCharacter() {
        if (!playerState.IsLoaded) {
            return;
        }

        // HomeWorld is a RowRef<World>; ValueNullable guards the unresolved case.
        // ToString() is used here rather than ExtractText() because Name is reached
        // through the RowRef indirection (ValueNullable?.Name), not via a direct
        // sheet GetRowOrDefault call. Both are valid on Lumina SeString columns;
        // ItemNameResolver uses ExtractText() on the same kind of column successfully.
        var world = playerState.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;
        Ledger.UpsertCharacter(new Character(
            playerState.ContentId,
            NameSanitizer.Sanitize(playerState.CharacterName),
            NameSanitizer.Sanitize(world)));
    }

    /// <summary>
    /// Marks the ledger as needing a save. Only the first call after a clean
    /// state actually records the tick; subsequent dirty calls before a save
    /// extend no further than the initial window.
    /// </summary>
    private void MarkDirty() {
        if (dirtySinceTick == 0) {
            dirtySinceTick = Environment.TickCount64;
        }
    }

    private void TrySave() {
        try {
            store.Save(Ledger);
            dirtySinceTick = 0;
            consecutiveSaveFailures = 0;
            saveFailureNotified = false;
        } catch (Exception exception) {
            consecutiveSaveFailures++;
            // Reschedule: reset the window so we retry after another debounce period.
            dirtySinceTick = Environment.TickCount64;
            log.Error(exception, "Ledger save failed ({Count} consecutive).", consecutiveSaveFailures);

            if (consecutiveSaveFailures >= SaveFailureNoticeThreshold && !saveFailureNotified) {
                saveFailureNotified = true;
                // JsonLedgerStore wraps save failures with the ledger path, so
                // exception.Message carries the file location for the user.
                chat.PrintError(
                    $"{ChatPrefix}Ledger could not be saved after {consecutiveSaveFailures} attempts: " +
                    $"{exception.Message} Sales remain tracked in memory until saving succeeds.");
            }
        }
    }
}
