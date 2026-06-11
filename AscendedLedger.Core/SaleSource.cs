namespace AscendedLedger;

/// <summary>
/// Provenance of a sale record: inferred from a listings diff, read from the
/// game's sale-history list, or an inferred record upgraded by history data.
/// Part of the external ledger.json contract.
/// </summary>
public enum SaleSource {
    Inferred,
    History,
    Merged,
}
