namespace AscendedLedger;

/// <summary>
/// Persistence boundary for the ledger. Defined in Core so domain consumers
/// depend on the abstraction; the plugin supplies the file-based implementation.
/// </summary>
public interface ILedgerStore {
    /// <summary>
    /// Loads the persisted ledger, recovering to an empty one (with the bad
    /// file backed up) when the stored data cannot be used.
    /// </summary>
    LedgerStoreLoadOutcome Load();

    /// <summary>Persists the ledger. Implementations must write atomically.</summary>
    void Save(Ledger ledger);
}
