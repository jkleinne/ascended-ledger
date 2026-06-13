namespace AscendedLedger;

/// <summary>
/// Result of a store load: always a usable ledger, plus what (if anything) it
/// recovered from and where the unusable file was backed up. MigratedFromSchemaVersion
/// is the version a one-time upgrade ran from (null when none ran).
/// </summary>
public sealed record LedgerStoreLoadOutcome(Ledger Ledger, LedgerLoadError RecoveredFromError, string? BackupPath, int? MigratedFromSchemaVersion = null);
