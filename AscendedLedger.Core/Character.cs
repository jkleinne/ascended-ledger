namespace AscendedLedger;

/// <summary>
/// A player character that owns retainers. Keyed by the immutable ContentId
/// because names and worlds can change.
/// </summary>
public sealed record Character(ulong ContentId, string Name, string World);
