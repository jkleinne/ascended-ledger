using System;
using System.Collections.Generic;

using Dalamud.Game.Network.Structures;
using Dalamud.Plugin.Services;

namespace AscendedLedger.Services;

/// <summary>
/// Holds the most recent live market tax rates. The game only sends rates when
/// interacting with a retainer vocate, so consumers fall back to the default
/// rate until a snapshot arrives.
/// </summary>
internal sealed class TaxRateService : IDisposable {
    private readonly IMarketBoard marketBoard;
    private readonly IPluginLog log;

    internal TaxRateService(IMarketBoard marketBoard, IPluginLog log) {
        this.marketBoard = marketBoard;
        this.log = log;
        marketBoard.TaxRatesReceived += OnTaxRatesReceived;
    }

    /// <summary>Raised after a new rate snapshot is stored.</summary>
    public event Action? RatesUpdated;

    /// <summary>Latest captured rates, or null before the first capture.</summary>
    public MarketTaxRatesSnapshot? Current { get; private set; }

    /// <inheritdoc/>
    public void Dispose() => marketBoard.TaxRatesReceived -= OnTaxRatesReceived;

    private void OnTaxRatesReceived(IMarketTaxRates rates) {
        Current = new MarketTaxRatesSnapshot(
            new Dictionary<Town, int> {
                [Town.LimsaLominsa] = (int)rates.LimsaLominsaTax,
                [Town.Gridania] = (int)rates.GridaniaTax,
                [Town.Uldah] = (int)rates.UldahTax,
                [Town.Ishgard] = (int)rates.IshgardTax,
                [Town.Kugane] = (int)rates.KuganeTax,
                [Town.Crystarium] = (int)rates.CrystariumTax,
                [Town.OldSharlayan] = (int)rates.SharlayanTax,
                [Town.Tuliyollal] = (int)rates.TuliyollalTax,
            },
            rates.ValidUntil);
        log.Debug("Market tax rates updated; valid until {ValidUntil}.", rates.ValidUntil);
        RatesUpdated?.Invoke();
    }
}
