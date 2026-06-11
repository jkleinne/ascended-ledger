# Ascended Ledger

A Dalamud plugin for Final Fantasy XIV that provides market and retainer-sales intelligence: your own retainer listings and completed sales are read from the game client and paired with board-wide market context (listings, sales history, velocity) from the [Universalis](https://universalis.app/) API.

## Installation

Add the custom plugin repository in Dalamud:

```text
https://raw.githubusercontent.com/jkleinne/ascended-plugins/master/pluginmaster.json
```

Then install **Ascended Ledger** from the plugin installer.

## Development

Requires the .NET 10 SDK and Dalamud development files. On Windows the SDK resolves them from the XIVLauncher dev path automatically; elsewhere point `DALAMUD_HOME` at an extracted [Dalamud distribution](https://goatcorp.github.io/dalamud-distrib/latest.zip).

```sh
dotnet restore AscendedLedger/AscendedLedger.csproj
dotnet build --no-restore -c Release AscendedLedger/AscendedLedger.csproj
```

## Releasing

Pushing a numeric tag such as `1.2.3` triggers the publish workflow: it builds the plugin, attaches `latest.zip` and a generated `pluginmaster.json` to a GitHub release, and syncs the entry into the [ascended-plugins](https://github.com/jkleinne/ascended-plugins) hub repository. The `Publish Testing` workflow publishes a `testing/x.y.z` prerelease to the testing channel via manual dispatch.
