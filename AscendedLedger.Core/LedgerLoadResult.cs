namespace AscendedLedger;

/// <summary>
/// Outcome of deserializing a ledger document: either a ledger, or a typed
/// error with a human-readable detail naming what failed (never file contents).
/// </summary>
public sealed record LedgerLoadResult(Ledger? Ledger, LedgerLoadError Error, string? Detail);
