namespace AscendedLedger;

/// <summary>
/// A retainer selling on the market, linked to its owning character so
/// multi-character views can group and filter.
/// </summary>
public sealed record Retainer(ulong RetainerId, ulong OwnerContentId, string Name, Town Town);
