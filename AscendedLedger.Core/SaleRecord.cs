using System;

namespace AscendedLedger;

/// <summary>
/// One completed retainer sale — the central row of the ledger.json contract.
/// IsTaxEstimated is true exactly when tax/net were not corroborated by the
/// retainer gil delta; consumers should treat flagged amounts as approximate.
/// </summary>
public sealed record SaleRecord {
    /// <summary>ContentId of the character whose retainer made this sale.</summary>
    public required ulong OwnerContentId { get; init; }

    /// <summary>Retainer that held the listing that sold.</summary>
    public required ulong RetainerId { get; init; }

    /// <summary>Game item id of the sold item.</summary>
    public required uint ItemId { get; init; }

    /// <summary>Number of units in the sold stack.</summary>
    public required int Quantity { get; init; }

    /// <summary>Per-unit price at which the stack sold.</summary>
    public required long UnitPrice { get; init; }

    /// <summary>Whether the sold item was high quality.</summary>
    public required bool IsHq { get; init; }

    /// <summary>Total price paid by the buyer before tax (Quantity × UnitPrice).</summary>
    public required long GrossGil { get; init; }

    /// <summary>Market tax withheld from GrossGil.</summary>
    public required long TaxGil { get; init; }

    /// <summary>Gil actually deposited into the retainer after tax (GrossGil − TaxGil).</summary>
    public required long NetGil { get; init; }

    /// <summary>True when TaxGil was estimated from the tax-rate table rather than corroborated by a gil delta.</summary>
    public required bool IsTaxEstimated { get; init; }

    /// <summary>UTC moment of sale or detection, depending on SoldAtPrecision.</summary>
    public required DateTime SoldAtUtc { get; init; }

    /// <summary>Whether SoldAtUtc is the exact sale time or only a detection bound.</summary>
    public required SoldAtPrecision SoldAtPrecision { get; init; }

    /// <summary>Buyer character name when available from history data; null for inferred-only records.</summary>
    public string? BuyerName { get; init; }

    /// <summary>How this record was produced: inferred from a diff, lifted from history, or merged from both.</summary>
    public required SaleSource Source { get; init; }
}
