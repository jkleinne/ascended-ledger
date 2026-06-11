namespace AscendedLedger;

/// <summary>
/// City a retainer sells in. Backing values mirror the game client's town ids;
/// unrecognized bytes map to <see cref="Unknown"/> so a game patch can never
/// crash capture, only degrade tax precision to the default rate.
/// </summary>
public enum Town : byte {
    Unknown = 0,
    LimsaLominsa = 1,
    Gridania = 2,
    Uldah = 3,
    Ishgard = 4,
    Kugane = 7,
    Crystarium = 10,
    OldSharlayan = 12,
    Tuliyollal = 14,
}
