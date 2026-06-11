using System.Collections.Generic;

using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AscendedLedger.Services;

/// <summary>
/// Resolves item ids to display names from the game's Item sheet, cached
/// because the UI asks every frame and sheet lookups are not free.
/// </summary>
internal sealed class ItemNameResolver {
    private readonly IDataManager dataManager;
    // Intentionally unbounded: the key space is the player's own item set.
    private readonly Dictionary<uint, string> cache = new();

    internal ItemNameResolver(IDataManager dataManager) {
        this.dataManager = dataManager;
    }

    /// <summary>Display name for an item id, or a placeholder for unknown ids.</summary>
    public string NameOf(uint itemId) {
        if (cache.TryGetValue(itemId, out var cached)) {
            return cached;
        }

        var name = dataManager.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ExtractText();
        var resolved = string.IsNullOrEmpty(name) ? $"Item #{itemId}" : name;
        cache[itemId] = resolved;
        return resolved;
    }
}
