# Ascended Ledger

A Dalamud plugin for Final Fantasy XIV that provides market and retainer-sales intelligence: your own retainer listings and completed sales are read from the game client and paired with board-wide market context (listings, sales history, velocity) from the [Universalis](https://universalis.app/) API.

## Installation

Add the custom plugin repository in Dalamud:

```text
https://raw.githubusercontent.com/jkleinne/ascended-plugins/master/pluginmaster.json
```

Then install **Ascended Ledger** from the plugin installer.

## Capturing data

Capture is passive: play normally and the plugin records what it sees.

- **Listings**: the active retainer's market listings and held gil are
  snapshotted whenever the retainer's sell list opens or refreshes.
- **Inferred sales**: listings that disappear between snapshots are recorded
  as sales (`source: "Inferred"`) with detection-moment timestamps
  (`soldAtPrecision: "DetectedAt"`).
- **Ground-truth sales**: opening a retainer's "View sale history" window
  captures real sale timestamps and buyer names (`source: "History"`);
  matching inferred records are upgraded in place (`source: "Merged"`,
  `soldAtPrecision: "Exact"`).

Opening every retainer's sale history by hand gets tedious. The
[Ascended Dagobert](https://github.com/jkleinne/ascended-dagobert) fork
(1.0.0.21+) can do it as part of a run you already make: enable
"Open Sale History During Auto Pinch" in its config and a full Auto Pinch run
briefly opens each retainer's sale history on the way through. The plugins
stay independent; either works without the other.

## Data & MCP contract

Ascended Ledger persists everything it captures to `ledger.json` in the plugin
config directory (`<Dalamud configs>/ascended-ledger/`). The file is a stable,
versioned contract intended for external tooling.

- `schemaVersion` (currently `1`): consumers must reject files with a higher
  version than they understand. Any breaking shape change bumps it.
- Top-level: `characters`, `retainers`, `listingSnapshots` (latest per
  retainer), `sales` (append-ordered), `taxRates` (latest live capture).
- All field names are camelCase (`ownerContentId`, `retainerGil`,
  `validUntilUtc`). Enums serialize as literal names: `source` is one of
  `"Inferred"`, `"History"`, `"Merged"`; `soldAtPrecision` is `"DetectedAt"`
  or `"Exact"`.
- The authoritative field list is the record set in `AscendedLedger.Core/`
  (`SaleRecord`, `Character`, `Retainer`, `ListingSnapshot`, `Listing`,
  `MarketTaxRatesSnapshot`); the serialized shape is those records, camelCased.
- Sale records carry gross/tax/net gil. `isTaxEstimated: true` means the
  amounts were not corroborated by the retainer's gil delta and are
  rate-based estimates. `soldAtPrecision: "DetectedAt"` means the timestamp is
  the detection moment (bounded by retainer-visit cadence), not the sale time.
- Listings carry an optional `firstSeenUtc` (UTC): the earliest snapshot
  observation at which that exact listing content (item, quantity, unit
  price, HQ) was continuously observed on its retainer. A reprice or
  quantity change restarts it; it is absent or null on data persisted before the
  field existed.
- All timestamps are UTC.
- Privacy: the file contains your character names/ids and buyer character
  names. Keep that in mind before syncing or sharing the config directory.

The plugin writes atomically (temp file + rename) and never overwrites a file
it cannot parse; unusable files (unparseable, wrong schema version, or above the
size cap) are backed up beside the original.

## Development

Requires the .NET 10 SDK and Dalamud development files. On Windows the SDK resolves them from the XIVLauncher dev path automatically; elsewhere point `DALAMUD_HOME` at an extracted [Dalamud distribution](https://goatcorp.github.io/dalamud-distrib/latest.zip).

```sh
dotnet restore AscendedLedger/AscendedLedger.csproj
dotnet build --no-restore -c Release AscendedLedger/AscendedLedger.csproj
```

## Releasing

Pushing a numeric tag such as `1.2.3` triggers the publish workflow: it builds the plugin, attaches `latest.zip` and a generated `pluginmaster.json` to a GitHub release, and syncs the entry into the [ascended-plugins](https://github.com/jkleinne/ascended-plugins) hub repository. The `Publish Testing` workflow publishes a `testing/x.y.z` prerelease to the testing channel via manual dispatch.
