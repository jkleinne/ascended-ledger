using System;
using System.Collections.Generic;
using System.Linq;

namespace AscendedLedger;

/// <summary>
/// Derives completed sales from two consecutive listing snapshots of one
/// retainer. Listings are compared by content (item, quantity, price, HQ),
/// never by slot, and the retainer's gil delta decides whether vanished
/// listings were sold (gil rose) or removed by the player (gil unchanged).
/// </summary>
public static class SaleInference {
    private const long MinimumUnitPrice = 1;
    private const int MinimumQuantity = 1;

    /// <summary>
    /// Returns sale records for listings present in <paramref name="previous"/> but
    /// absent from <paramref name="current"/>, priced per the spec's gil-delta rules.
    /// Records are flagged IsTaxEstimated when the delta does not corroborate them.
    /// Zero-priced or zero-quantity entries (garbage memory) are never sales.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="previous"/> and <paramref name="current"/> have
    /// different RetainerIds. Mismatched snapshots are a caller bug and must
    /// fail loudly so corrupt records are never silently written to the ledger.
    /// </exception>
    public static IReadOnlyList<SaleRecord> InferSales(
        ListingSnapshot previous,
        ListingSnapshot current,
        int taxRatePercent,
        ulong ownerContentId) {
        if (previous.RetainerId != current.RetainerId) {
            throw new ArgumentException(
                $"Snapshot retainer mismatch: previous={previous.RetainerId}, current={current.RetainerId}.");
        }

        var gilDelta = current.RetainerGil - previous.RetainerGil;
        if (gilDelta <= 0) {
            return Array.Empty<SaleRecord>();
        }

        var vanished = VanishedListings(previous.Listings, current.Listings)
            .Where(l => l.UnitPrice >= MinimumUnitPrice && l.Quantity >= MinimumQuantity)
            .ToList();
        if (vanished.Count == 0) {
            return Array.Empty<SaleRecord>();
        }

        var expectedNetTotal = vanished.Sum(l =>
            ProceedsCalculator.Net(
                ProceedsCalculator.Gross(l.Quantity, l.UnitPrice),
                taxRatePercent));

        if (gilDelta == expectedNetTotal) {
            return vanished
                .Select(l => BuildRecord(l, taxRatePercent, isTaxEstimated: false, current.ObservedAtUtc, current.RetainerId, ownerContentId))
                .ToList();
        }

        if (vanished.Count == 1 && IsPlausibleSingleNet(vanished[0], gilDelta)) {
            var gross = ProceedsCalculator.Gross(vanished[0].Quantity, vanished[0].UnitPrice);
            return new[] {
                BuildRecord(vanished[0], taxRatePercent, isTaxEstimated: false, current.ObservedAtUtc, current.RetainerId, ownerContentId) with {
                    NetGil = gilDelta,
                    TaxGil = gross - gilDelta,
                },
            };
        }

        return vanished
            .Select(l => BuildRecord(l, taxRatePercent, isTaxEstimated: true, current.ObservedAtUtc, current.RetainerId, ownerContentId))
            .ToList();
    }

    private static bool IsPlausibleSingleNet(Listing listing, long gilDelta) {
        var gross = ProceedsCalculator.Gross(listing.Quantity, listing.UnitPrice);
        var minimumNet = ProceedsCalculator.Net(gross, ProceedsCalculator.MaxTaxRatePercent);
        return gilDelta >= minimumNet && gilDelta <= gross;
    }

    private static SaleRecord BuildRecord(
        Listing listing,
        int taxRatePercent,
        bool isTaxEstimated,
        DateTime observedAtUtc,
        ulong retainerId,
        ulong ownerContentId) {
        var gross = ProceedsCalculator.Gross(listing.Quantity, listing.UnitPrice);
        var tax = ProceedsCalculator.Tax(gross, taxRatePercent);
        return new SaleRecord {
            OwnerContentId = ownerContentId,
            RetainerId = retainerId,
            ItemId = listing.ItemId,
            Quantity = listing.Quantity,
            UnitPrice = listing.UnitPrice,
            IsHq = listing.IsHq,
            GrossGil = gross,
            TaxGil = tax,
            NetGil = gross - tax,
            IsTaxEstimated = isTaxEstimated,
            SoldAtUtc = observedAtUtc,
            SoldAtPrecision = SoldAtPrecision.DetectedAt,
            Source = SaleSource.Inferred,
        };
    }

    private static List<Listing> VanishedListings(
        IReadOnlyList<Listing> previous,
        IReadOnlyList<Listing> current) {
        var remaining = current
            .GroupBy(l => l.ContentKey())
            .ToDictionary(g => g.Key, g => g.Count());

        var vanished = new List<Listing>();
        foreach (var listing in previous) {
            var key = listing.ContentKey();
            if (remaining.TryGetValue(key, out var count) && count > 0) {
                remaining[key] = count - 1;
            }
            else {
                vanished.Add(listing);
            }
        }

        return vanished;
    }
}
