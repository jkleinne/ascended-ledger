using System;
using System.Collections.Generic;
using System.Linq;

namespace AscendedLedger;

/// <summary>
/// Aggregate root for all ledger state. The plugin holds exactly one instance,
/// mutated only through these methods on the framework thread, so invariants
/// (latest-snapshot-per-retainer retention, append-only sales, idempotent
/// history merges) are enforced in one place.
/// </summary>
public sealed class Ledger {
    /// <summary>Version of the ledger.json contract this build reads and writes.</summary>
    public const int SchemaVersion = 1;

    private readonly Dictionary<ulong, Character> characters = new();
    private readonly Dictionary<ulong, Retainer> retainers = new();
    private readonly Dictionary<ulong, ListingSnapshot> latestSnapshots = new();
    private List<SaleRecord> sales = new();

    /// <summary>Live tax rates last captured from the game, if any.</summary>
    public MarketTaxRatesSnapshot? TaxRates { get; private set; }

    /// <summary>Known characters keyed by ContentId.</summary>
    public IReadOnlyDictionary<ulong, Character> CharactersById => characters;

    /// <summary>Known retainers keyed by retainer id.</summary>
    public IReadOnlyDictionary<ulong, Retainer> RetainersById => retainers;

    /// <summary>Latest listing snapshot per retainer (the only listings retained).</summary>
    public IReadOnlyDictionary<ulong, ListingSnapshot> LatestSnapshotsByRetainerId => latestSnapshots;

    /// <summary>All completed sales, append-ordered.</summary>
    public IReadOnlyList<SaleRecord> Sales => sales;

    /// <summary>Adds or refreshes a character.</summary>
    public void UpsertCharacter(Character character) => characters[character.ContentId] = character;

    /// <summary>Adds or refreshes a retainer.</summary>
    public void UpsertRetainer(Retainer retainer) => retainers[retainer.RetainerId] = retainer;

    /// <summary>Replaces the live tax-rate snapshot.</summary>
    public void SetTaxRates(MarketTaxRatesSnapshot rates) => TaxRates = rates;

    /// <summary>
    /// Records a fresh snapshot, inferring sales against the previous one when
    /// it exists and stamping listing first-seen continuity before storing.
    /// Returns the newly inferred sales (empty on first sighting).
    /// </summary>
    public IReadOnlyList<SaleRecord> ApplySnapshot(ListingSnapshot snapshot, int taxRatePercent, ulong ownerContentId) {
        IReadOnlyList<SaleRecord> inferred = Array.Empty<SaleRecord>();
        latestSnapshots.TryGetValue(snapshot.RetainerId, out var previous);
        if (previous is not null) {
            inferred = SaleInference.InferSales(previous, snapshot, taxRatePercent, ownerContentId);
            sales.AddRange(inferred);
        }

        latestSnapshots[snapshot.RetainerId] = ListingAges.CarryForward(previous, snapshot);
        return inferred;
    }

    /// <summary>
    /// Reconciles sale-history ground truth into the ledger. Returns how many
    /// records were added or upgraded (0 when the merge was a no-op).
    /// </summary>
    public int ApplyHistory(ulong retainerId, ulong ownerContentId, IReadOnlyList<HistorySale> entries, int taxRatePercent) {
        var merged = SaleMerger.Merge(sales, entries, ownerContentId, retainerId, taxRatePercent);
        var changed = merged.Except(sales).Count();
        sales = merged.ToList();
        return changed;
    }

    /// <summary>
    /// Rebuilds a ledger from persisted (already validated) parts. Used only by
    /// deserialization; capture paths go through the Apply methods.
    /// </summary>
    public static Ledger Restore(IEnumerable<Character> characters, IEnumerable<Retainer> retainers, IEnumerable<ListingSnapshot> snapshots, IEnumerable<SaleRecord> sales, MarketTaxRatesSnapshot? taxRates) {
        var ledger = new Ledger();
        foreach (var character in characters) {
            ledger.UpsertCharacter(character);
        }

        foreach (var retainer in retainers) {
            ledger.UpsertRetainer(retainer);
        }

        foreach (var snapshot in snapshots) {
            ledger.latestSnapshots[snapshot.RetainerId] = snapshot;
        }

        ledger.sales = sales.ToList();
        if (taxRates is not null) {
            ledger.SetTaxRates(taxRates);
        }

        return ledger;
    }
}
