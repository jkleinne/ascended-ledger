namespace AscendedLedger;

/// <summary>
/// Outcome of deserializing a ledger document: either a ledger, or a typed
/// error with a human-readable detail naming what failed (never file contents).
/// MigratedFromSchemaVersion is the version a one-time upgrade ran from (null
/// when none ran), so the store can preserve the original file.
/// </summary>
public sealed record LedgerLoadResult(Ledger? Ledger, LedgerLoadError Error, string? Detail, int? MigratedFromSchemaVersion = null);
