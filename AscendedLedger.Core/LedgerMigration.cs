using System.Collections.Generic;
using System.Linq;

namespace AscendedLedger;

/// <summary>
/// One-shot upgrade of a schemaVersion-1 sales list to version-2 semantics. v1
/// wrote each Source=History record with the after-tax net mislabeled as GrossGil
/// and the net double-taxed. This reconstructs those rows as HistorySale entries
/// (their stored GrossGil is the true net) and re-runs the corrected SaleMerger,
/// which collapses inferred/history duplicates into Merged rows and rebuilds
/// history-only breakdowns. Pure transform; no I/O. Inferred and Merged rows are
/// already correct and pass through untouched.
/// </summary>
public static class LedgerMigration {
    /// <summary>
    /// Returns the corrected sales list. <paramref name="retainers"/> resolves each
    /// retainer's town (for the estimated breakdown of history-only rows) and
    /// <paramref name="taxRates"/> the rate; unknown towns fall back to the default.
    /// </summary>
    public static IReadOnlyList<SaleRecord> MigrateSalesV1ToV2(
        IReadOnlyList<SaleRecord> sales,
        IReadOnlyList<Retainer> retainers,
        MarketTaxRatesSnapshot? taxRates) {
        var townByRetainer = retainers.ToDictionary(r => r.RetainerId, r => r.Town);
        var working = sales.Where(s => s.Source != SaleSource.History).ToList();

        foreach (var group in sales
            .Where(s => s.Source == SaleSource.History)
            .GroupBy(s => (s.RetainerId, s.OwnerContentId))) {
            var entries = group
                .Select(s => new HistorySale(s.ItemId, s.Quantity, s.GrossGil, s.IsHq, s.SoldAtUtc, s.BuyerName ?? string.Empty))
                .ToList();
            var town = townByRetainer.TryGetValue(group.Key.RetainerId, out var resolved) ? resolved : Town.Unknown;
            var rate = taxRates?.RateFor(town) ?? ProceedsCalculator.DefaultTaxRatePercent;
            working = SaleMerger.Merge(working, entries, group.Key.OwnerContentId, group.Key.RetainerId, rate).ToList();
        }

        return working;
    }
}
