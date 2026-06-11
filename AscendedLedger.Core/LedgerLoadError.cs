namespace AscendedLedger;

/// <summary>Typed reasons a persisted ledger could not be loaded as-is.</summary>
public enum LedgerLoadError {
    None,
    InvalidJson,
    UnsupportedSchemaVersion,
    StructuralViolation,
}
