using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AscendedLedger;

/// <summary>
/// Serializes the ledger to its versioned JSON contract and validates
/// untrusted documents on the way back in. camelCase + enum names keep the
/// contract readable for external consumers; unknown fields are ignored so
/// forward-compatible additions never break older readers.
/// </summary>
public static class LedgerSerializer {
    /// <summary>Cap on characters in one document.</summary>
    public const int MaxCharacters = 64;

    /// <summary>Cap on retainers in one document (10 per character ceiling).</summary>
    public const int MaxRetainers = 640;

    /// <summary>Cap on sale records in one document.</summary>
    public const int MaxSaleRecords = 200_000;

    /// <summary>Cap on any persisted name string.</summary>
    public const int MaxNameLength = 64;

    /// <summary>Game cap on a listing's unit price.</summary>
    public const long MaxUnitPrice = 999_999_999;

    /// <summary>Game cap on a listing's stack size.</summary>
    public const int MaxQuantity = 9_999;

    /// <summary>The legacy schema version migrated forward on load (history Price read as gross).</summary>
    private const int SchemaVersionV1 = 1;

    private static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Writes the ledger as schemaVersion-1 contract JSON.</summary>
    public static string Serialize(Ledger ledger) {
        var document = new LedgerDocument {
            SchemaVersion = Ledger.SchemaVersion,
            Characters = ledger.CharactersById.Values.ToList(),
            Retainers = ledger.RetainersById.Values.ToList(),
            ListingSnapshots = ledger.LatestSnapshotsByRetainerId.Values.ToList(),
            Sales = ledger.Sales.ToList(),
            TaxRates = ledger.TaxRates,
        };
        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>Parses and validates contract JSON. Never throws on bad input.</summary>
    public static LedgerLoadResult Deserialize(string json) {
        LedgerDocument? document;
        try {
            document = JsonSerializer.Deserialize<LedgerDocument>(json, Options);
        } catch (JsonException exception) {
            return new LedgerLoadResult(null, LedgerLoadError.InvalidJson, exception.Message);
        }

        if (document is null) {
            return new LedgerLoadResult(null, LedgerLoadError.InvalidJson, "Document deserialized to null.");
        }

        if (document.SchemaVersion != Ledger.SchemaVersion && document.SchemaVersion != SchemaVersionV1) {
            return new LedgerLoadResult(null, LedgerLoadError.UnsupportedSchemaVersion, $"schemaVersion {document.SchemaVersion} is not {Ledger.SchemaVersion}.");
        }

        // An explicit JSON null overwrites the DTO's initializers, so a hand-edited
        // or partial document can still hand us null collections. Normalize them all
        // here: this is the single boundary past which every collection on the
        // document (and every snapshot's Listings) is guaranteed non-null.
        document.Characters ??= new List<Character>();
        document.Retainers ??= new List<Retainer>();
        document.ListingSnapshots ??= new List<ListingSnapshot>();
        document.Sales ??= new List<SaleRecord>();
        document.ListingSnapshots = document.ListingSnapshots
            .Select(s => s.Listings is null ? s with { Listings = new List<Listing>() } : s)
            .ToList();
        if (document.TaxRates is { RatePercentByTown: null }) {
            document.TaxRates = document.TaxRates with { RatePercentByTown = new Dictionary<Town, int>() };
        }

        var violation = FindStructuralViolation(document);
        if (violation is not null) {
            return new LedgerLoadResult(null, LedgerLoadError.StructuralViolation, violation);
        }

        int? migratedFrom = null;
        if (document.SchemaVersion == SchemaVersionV1) {
            document.Sales = LedgerMigration.MigrateSalesV1ToV2(document.Sales, document.Retainers, document.TaxRates).ToList();
            migratedFrom = SchemaVersionV1;

            // Defense in depth: GrossFromNet keeps gross >= net for well-formed v1
            // data, but a hand-edited file could carry a History net above the
            // per-unit cap, which the gross clamp would invert. Re-validate so a
            // corrupt migration is treated as an unusable file (backed up), never loaded.
            var migrationViolation = FindStructuralViolation(document);
            if (migrationViolation is not null) {
                return new LedgerLoadResult(null, LedgerLoadError.StructuralViolation, migrationViolation);
            }
        }

        var ledger = Ledger.Restore(
            document.Characters.Select(c => c with { Name = NameSanitizer.Sanitize(c.Name), World = NameSanitizer.Sanitize(c.World) }),
            document.Retainers.Select(r => r with { Name = NameSanitizer.Sanitize(r.Name) }),
            document.ListingSnapshots.Select(NormalizeSnapshot),
            document.Sales.Select(NormalizeSale),
            document.TaxRates);
        return new LedgerLoadResult(ledger, LedgerLoadError.None, null, migratedFrom);
    }

    private static string? FindStructuralViolation(LedgerDocument document) {
        if (document.Characters.Count > MaxCharacters) {
            return $"characters count {document.Characters.Count} exceeds cap {MaxCharacters}";
        }

        if (document.Retainers.Count > MaxRetainers) {
            return $"retainers count {document.Retainers.Count} exceeds cap {MaxRetainers}";
        }

        if (document.Sales.Count > MaxSaleRecords) {
            return $"sales count {document.Sales.Count} exceeds cap {MaxSaleRecords}";
        }

        foreach (var snapshot in document.ListingSnapshots) {
            if (snapshot.Listings.Count > ListingSnapshot.MaxSlots) {
                return $"snapshot for retainer {snapshot.RetainerId} has {snapshot.Listings.Count} listings (cap {ListingSnapshot.MaxSlots})";
            }

            if (snapshot.RetainerGil < 0) {
                return $"snapshot for retainer {snapshot.RetainerId} has negative retainerGil";
            }

            foreach (var listing in snapshot.Listings) {
                if (listing.ItemId == 0 || listing.Quantity < 1 || listing.Quantity > MaxQuantity || listing.UnitPrice < 1 || listing.UnitPrice > MaxUnitPrice) {
                    return $"listing in snapshot for retainer {snapshot.RetainerId} has out-of-range itemId/quantity/unitPrice";
                }
            }
        }

        foreach (var sale in document.Sales) {
            if (sale.ItemId == 0 || sale.Quantity < 1 || sale.Quantity > MaxQuantity || sale.UnitPrice < 0 || sale.UnitPrice > MaxUnitPrice) {
                return "sale record has out-of-range itemId/quantity/unitPrice";
            }

            if (sale.GrossGil < 0 || sale.TaxGil < 0 || sale.NetGil < 0 || sale.NetGil > sale.GrossGil) {
                return "sale record has inconsistent grossGil/taxGil/netGil";
            }
        }

        return null;
    }

    private static ListingSnapshot NormalizeSnapshot(ListingSnapshot snapshot) =>
        snapshot with {
            ObservedAtUtc = DateTime.SpecifyKind(snapshot.ObservedAtUtc, DateTimeKind.Utc),
            Listings = snapshot.Listings
                .Select(l => l.FirstSeenUtc is { } firstSeen ? l with { FirstSeenUtc = DateTime.SpecifyKind(firstSeen, DateTimeKind.Utc) } : l)
                .ToList(),
        };

    private static SaleRecord NormalizeSale(SaleRecord sale) =>
        sale with {
            SoldAtUtc = DateTime.SpecifyKind(sale.SoldAtUtc, DateTimeKind.Utc),
            BuyerName = sale.BuyerName is null ? null : NameSanitizer.Sanitize(sale.BuyerName),
        };
}
