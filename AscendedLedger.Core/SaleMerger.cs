using System;
using System.Collections.Generic;
using System.Linq;

namespace AscendedLedger;

/// <summary>
/// Reconciles ground-truth sale-history entries into the sale list. History
/// gives real timestamps and buyers but only the last 20 sales per retainer,
/// so it upgrades diff-inferred records when they describe the same sale and
/// inserts the sales the diff never saw. Idempotent: re-applying the same
/// entries is a no-op, keyed on (retainer, item, sold-at, gross, qty, buyer).
/// </summary>
public static class SaleMerger {
    /// <summary>
    /// Returns a new sale list with <paramref name="history"/> reconciled in.
    /// <paramref name="taxRatePercent"/> prices unmatched entries (estimated,
    /// since only gross is ground truth).
    /// </summary>
    public static IReadOnlyList<SaleRecord> Merge(
        IReadOnlyList<SaleRecord> existing,
        IReadOnlyList<HistorySale> history,
        ulong ownerContentId,
        ulong retainerId,
        int taxRatePercent) {
        var result = existing.ToList();

        foreach (var entry in history) {
            if (result.Any(r => IsSameHistorySale(r, entry, retainerId))) {
                continue;
            }

            var matchIndex = FindClosestInferredMatch(result, entry, retainerId);
            if (matchIndex >= 0) {
                result[matchIndex] = result[matchIndex] with {
                    SoldAtUtc = entry.SoldAtUtc,
                    SoldAtPrecision = SoldAtPrecision.Exact,
                    BuyerName = entry.BuyerName,
                    Source = SaleSource.Merged,
                };
            }
            else {
                result.Add(BuildHistoryRecord(entry, ownerContentId, retainerId, taxRatePercent));
            }
        }

        return result;
    }

    private static bool IsSameHistorySale(SaleRecord record, HistorySale entry, ulong retainerId) =>
        record.SoldAtPrecision == SoldAtPrecision.Exact
        && record.RetainerId == retainerId
        && record.ItemId == entry.ItemId
        && record.Quantity == entry.Quantity
        && record.GrossGil == entry.GrossGil
        && record.SoldAtUtc == entry.SoldAtUtc
        && string.Equals(record.BuyerName, entry.BuyerName, StringComparison.Ordinal);

    private static int FindClosestInferredMatch(List<SaleRecord> records, HistorySale entry, ulong retainerId) {
        var bestIndex = -1;
        for (var i = 0; i < records.Count; i++) {
            var record = records[i];
            var isCandidate = record.Source == SaleSource.Inferred
                && record.RetainerId == retainerId
                && record.ItemId == entry.ItemId
                && record.Quantity == entry.Quantity
                && record.IsHq == entry.IsHq
                && record.GrossGil == entry.GrossGil
                && entry.SoldAtUtc <= record.SoldAtUtc;
            if (!isCandidate) {
                continue;
            }

            if (bestIndex < 0 || record.SoldAtUtc < records[bestIndex].SoldAtUtc) {
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static SaleRecord BuildHistoryRecord(
        HistorySale entry,
        ulong ownerContentId,
        ulong retainerId,
        int taxRatePercent) {
        var tax = ProceedsCalculator.Tax(entry.GrossGil, taxRatePercent);
        return new SaleRecord {
            OwnerContentId = ownerContentId,
            RetainerId = retainerId,
            ItemId = entry.ItemId,
            Quantity = entry.Quantity,
            UnitPrice = entry.Quantity > 0 ? entry.GrossGil / entry.Quantity : entry.GrossGil,
            IsHq = entry.IsHq,
            GrossGil = entry.GrossGil,
            TaxGil = tax,
            NetGil = entry.GrossGil - tax,
            IsTaxEstimated = true,
            SoldAtUtc = entry.SoldAtUtc,
            SoldAtPrecision = SoldAtPrecision.Exact,
            BuyerName = entry.BuyerName,
            Source = SaleSource.History,
        };
    }
}
