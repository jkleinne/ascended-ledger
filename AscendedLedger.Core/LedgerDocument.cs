using System.Collections.Generic;

namespace AscendedLedger;

/// <summary>
/// Serialization shape of ledger.json, schemaVersion 2 — the documented
/// contract consumed by external tooling (the ffxiv-market MCP server).
/// Changing any member name or shape requires a SchemaVersion bump.
/// </summary>
public sealed class LedgerDocument {
    /// <summary>
    /// Contract version. A reader accepts exactly its own schema version and treats
    /// any mismatch (higher or lower) as recoverable-unusable rather than parsing it.
    /// Consumers of the file are advised to reject versions higher than they know.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>All characters the ledger has seen.</summary>
    public List<Character> Characters { get; set; } = new();

    /// <summary>All retainers, linked to characters via OwnerContentId.</summary>
    public List<Retainer> Retainers { get; set; } = new();

    /// <summary>Latest listing snapshot per retainer.</summary>
    public List<ListingSnapshot> ListingSnapshots { get; set; } = new();

    /// <summary>Completed sales, append-ordered.</summary>
    public List<SaleRecord> Sales { get; set; } = new();

    /// <summary>Live tax rates last captured, if any.</summary>
    public MarketTaxRatesSnapshot? TaxRates { get; set; }
}
