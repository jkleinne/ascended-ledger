namespace AscendedLedger;

/// <summary>
/// Whether SoldAtUtc is the real sale moment (from history data) or merely the
/// moment the diff detected it (bounded by retainer-visit cadence).
/// Part of the external ledger.json contract.
/// </summary>
public enum SoldAtPrecision {
    DetectedAt,
    Exact,
}
