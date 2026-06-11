using Dalamud.Configuration;

namespace AscendedLedger;

/// <summary>
/// Persisted settings that Dalamud serializes between plugin sessions.
/// </summary>
public sealed class PluginConfiguration : IPluginConfiguration {
    private const int CurrentVersion = 1;

    /// <summary>
    /// Stores the config schema version so future releases can migrate older files.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;
}
