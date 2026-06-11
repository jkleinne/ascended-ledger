using System.Linq;

namespace AscendedLedger;

/// <summary>
/// Bounds and cleans name strings crossing trust boundaries (game memory in,
/// ledger file in). Control characters can break rendering and downstream
/// consumers; length caps bound file growth from corrupt input.
/// </summary>
public static class NameSanitizer {
    /// <summary>Strips control characters and truncates to the contract's name cap.</summary>
    public static string Sanitize(string value) {
        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Length <= LedgerSerializer.MaxNameLength ? cleaned : cleaned[..LedgerSerializer.MaxNameLength];
    }
}
