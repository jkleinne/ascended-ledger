using System;
using System.Collections.Generic;
using System.Linq;
using AscendedLedger;
using Xunit;

public class LedgerSerializerTests {
    private static Ledger ExercisedLedger() {
        var ledger = new Ledger();
        ledger.UpsertCharacter(new Character(1001, "Test Char", "Sargatanas"));
        ledger.UpsertRetainer(new Retainer(42, 1001, "Retainer One", Town.LimsaLominsa));
        ledger.SetTaxRates(new MarketTaxRatesSnapshot(
            new Dictionary<Town, int> { [Town.LimsaLominsa] = 5 },
            new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc)));
        ledger.ApplySnapshot(new ListingSnapshot(42, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), 0, new[] { new Listing(0, 100, 1, 10_000, false) }), 5, 1001);
        ledger.ApplySnapshot(new ListingSnapshot(42, new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), 9_500, Array.Empty<Listing>()), 5, 1001);
        return ledger;
    }

    [Fact]
    public void SerializeDeserialize_RoundTripsExactly() {
        var original = ExercisedLedger();

        var result = LedgerSerializer.Deserialize(LedgerSerializer.Serialize(original));

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.NotNull(result.Ledger);
        Assert.Equal(original.Sales, result.Ledger!.Sales);
        Assert.Equal(original.CharactersById, result.Ledger.CharactersById);
        Assert.Equal(original.RetainersById, result.Ledger.RetainersById);
        Assert.Equal(original.TaxRates!.ValidUntilUtc, result.Ledger.TaxRates!.ValidUntilUtc);
        var snapshot = result.Ledger.LatestSnapshotsByRetainerId[42];
        Assert.Equal(original.LatestSnapshotsByRetainerId[42].Listings, snapshot.Listings);
        Assert.Equal(DateTimeKind.Utc, snapshot.ObservedAtUtc.Kind);
    }

    [Fact]
    public void Serialize_EmitsVersionAndEnumNames() {
        var json = LedgerSerializer.Serialize(ExercisedLedger());

        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"Inferred\"", json);
        Assert.DoesNotContain("\"source\": 0", json);
    }

    [Fact]
    public void Deserialize_FutureSchemaVersion_IsUnsupported() {
        var result = LedgerSerializer.Deserialize("{\"schemaVersion\": 2}");

        Assert.Equal(LedgerLoadError.UnsupportedSchemaVersion, result.Error);
        Assert.Null(result.Ledger);
    }

    [Fact]
    public void Deserialize_MalformedJson_IsInvalidJson() {
        Assert.Equal(LedgerLoadError.InvalidJson, LedgerSerializer.Deserialize("{not json").Error);
        Assert.Equal(LedgerLoadError.InvalidJson, LedgerSerializer.Deserialize(string.Empty).Error);
    }

    [Fact]
    public void Deserialize_NegativeGil_IsStructuralViolation() {
        var json = LedgerSerializer.Serialize(ExercisedLedger()).Replace("\"netGil\": 9500", "\"netGil\": -5");

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.StructuralViolation, result.Error);
        Assert.Contains("netGil", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_UnknownFields_AreIgnored() {
        // Inject a field this build does not know right after the opening brace.
        var json = LedgerSerializer.Serialize(new Ledger()).Insert(1, "\"someFutureField\": true,");

        Assert.Equal(LedgerLoadError.None, LedgerSerializer.Deserialize(json).Error);
    }

    [Fact]
    public void Deserialize_TooManyCharacters_IsStructuralViolation() {
        var ledger = new Ledger();
        for (var i = 0; i <= LedgerSerializer.MaxCharacters; i++) {
            ledger.UpsertCharacter(new Character((ulong)(i + 1), $"Char{i}", "World"));
        }

        var result = LedgerSerializer.Deserialize(LedgerSerializer.Serialize(ledger));

        Assert.Equal(LedgerLoadError.StructuralViolation, result.Error);
        Assert.Contains("characters", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_ControlCharactersInNames_AreStripped() {
        var ledger = new Ledger();
        ledger.UpsertCharacter(new Character(1, "Bad\u0001Name", "Wor\u0007ld"));

        var result = LedgerSerializer.Deserialize(LedgerSerializer.Serialize(ledger));

        Assert.Equal("BadName", result.Ledger!.CharactersById[1].Name);
        Assert.Equal("World", result.Ledger.CharactersById[1].World);
    }

    [Fact]
    public void NameSanitizer_TruncatesAndStripsControls() {
        Assert.Equal("ab", NameSanitizer.Sanitize("a\u0001b"));
        Assert.Equal(LedgerSerializer.MaxNameLength, NameSanitizer.Sanitize(new string('x', 500)).Length);
    }

    [Fact]
    public void Deserialize_NormalizesDateTimeKindToUtc() {
        var ledger = new Ledger();
        ledger.ApplyHistory(42, 1001, new[] { new HistorySale(100, 1, 10_000, false, new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc), "Buyer Name") }, 5);

        var result = LedgerSerializer.Deserialize(LedgerSerializer.Serialize(ledger));

        Assert.Equal(DateTimeKind.Utc, result.Ledger!.Sales[0].SoldAtUtc.Kind);
    }

    [Fact]
    public void Deserialize_NullCollections_AreStructurallyEmpty() {
        var result = LedgerSerializer.Deserialize("{\"schemaVersion\": 1, \"sales\": null, \"characters\": null, \"retainers\": null, \"listingSnapshots\": null, \"taxRates\": null}");

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.Empty(result.Ledger!.Sales);
        Assert.Empty(result.Ledger.CharactersById);
    }

    [Fact]
    public void Deserialize_NullListingsInSnapshot_NeverThrows() {
        var json = "{\"schemaVersion\": 1, \"listingSnapshots\": [{\"retainerId\": 42, \"observedAtUtc\": \"2026-06-01T00:00:00Z\", \"retainerGil\": 0, \"listings\": null}]}";

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.Empty(result.Ledger!.LatestSnapshotsByRetainerId[42].Listings);
    }

    [Fact]
    public void RateFor_MissingTown_FallsBackToDefault() {
        var snapshot = new MarketTaxRatesSnapshot(new Dictionary<Town, int> { [Town.Kugane] = 3 }, DateTime.UnixEpoch);

        Assert.Equal(3, snapshot.RateFor(Town.Kugane));
        Assert.Equal(ProceedsCalculator.DefaultTaxRatePercent, snapshot.RateFor(Town.Ishgard));
    }

    [Fact]
    public void Deserialize_NullTaxRateDictionary_IsStructurallyEmpty() {
        var json = "{\"schemaVersion\": 1, \"taxRates\": {\"ratePercentByTown\": null, \"validUntilUtc\": \"2026-06-08T00:00:00Z\"}}";

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.Equal(ProceedsCalculator.DefaultTaxRatePercent, result.Ledger!.TaxRates!.RateFor(Town.LimsaLominsa));
    }

    [Fact]
    public void Deserialize_NullLeafNames_AreStructurallyEmpty() {
        var json = "{\"schemaVersion\": 1, \"characters\": [{\"contentId\": 1, \"name\": null, \"world\": null}], \"retainers\": [{\"retainerId\": 42, \"ownerContentId\": 1, \"name\": null, \"town\": \"LimsaLominsa\"}]}";

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.Equal(string.Empty, result.Ledger!.CharactersById[1].Name);
        Assert.Equal(string.Empty, result.Ledger.RetainersById[42].Name);
    }

    [Fact]
    public void NameSanitizer_NullInput_ReturnsEmpty() {
        Assert.Equal(string.Empty, NameSanitizer.Sanitize(null));
    }

    [Fact]
    public void SerializeDeserialize_PreservesFirstSeenUtcWithUtcKind() {
        var firstSeen = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var ledger = Ledger.Restore(
            Array.Empty<Character>(),
            Array.Empty<Retainer>(),
            new[] { new ListingSnapshot(42, new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), 0, new[] { new Listing(0, 100, 1, 10_000, false, firstSeen) }) },
            Array.Empty<SaleRecord>(),
            null);

        var result = LedgerSerializer.Deserialize(LedgerSerializer.Serialize(ledger));

        var listing = result.Ledger!.LatestSnapshotsByRetainerId[42].Listings[0];
        Assert.Equal(firstSeen, listing.FirstSeenUtc);
        Assert.Equal(DateTimeKind.Utc, listing.FirstSeenUtc!.Value.Kind);
    }

    [Fact]
    public void Deserialize_LegacyListingWithoutFirstSeen_LoadsNull() {
        var json = "{\"schemaVersion\": 1, \"listingSnapshots\": [{\"retainerId\": 42, \"observedAtUtc\": \"2026-06-01T00:00:00Z\", \"retainerGil\": 0, \"listings\": [{\"slot\": 0, \"itemId\": 100, \"quantity\": 1, \"unitPrice\": 10000, \"isHq\": false}]}]}";

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.Null(result.Ledger!.LatestSnapshotsByRetainerId[42].Listings[0].FirstSeenUtc);
    }

    [Fact]
    public void Deserialize_ExplicitNullFirstSeen_LoadsNull() {
        var json = "{\"schemaVersion\": 1, \"listingSnapshots\": [{\"retainerId\": 42, \"observedAtUtc\": \"2026-06-01T00:00:00Z\", \"retainerGil\": 0, \"listings\": [{\"slot\": 0, \"itemId\": 100, \"quantity\": 1, \"unitPrice\": 10000, \"isHq\": false, \"firstSeenUtc\": null}]}]}";

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.Null(result.Ledger!.LatestSnapshotsByRetainerId[42].Listings[0].FirstSeenUtc);
    }

    [Fact]
    public void Deserialize_ExtremeFirstSeen_LoadsWithoutError() {
        var json = "{\"schemaVersion\": 1, \"listingSnapshots\": [{\"retainerId\": 42, \"observedAtUtc\": \"2026-06-01T00:00:00Z\", \"retainerGil\": 0, \"listings\": [{\"slot\": 0, \"itemId\": 100, \"quantity\": 1, \"unitPrice\": 10000, \"isHq\": false, \"firstSeenUtc\": \"0001-01-01T00:00:00Z\"}]}]}";

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.Equal(DateTimeKind.Utc, result.Ledger!.LatestSnapshotsByRetainerId[42].Listings[0].FirstSeenUtc!.Value.Kind);
    }

    [Fact]
    public void Deserialize_TooManyListingsInSnapshot_IsStructuralViolation() {
        var listings = string.Join(",", Enumerable.Range(0, ListingSnapshot.MaxSlots + 1)
            .Select(slot => $"{{\"slot\": {slot}, \"itemId\": 100, \"quantity\": 1, \"unitPrice\": 10000, \"isHq\": false}}"));
        var json = $"{{\"schemaVersion\": 1, \"listingSnapshots\": [{{\"retainerId\": 42, \"observedAtUtc\": \"2026-06-01T00:00:00Z\", \"retainerGil\": 0, \"listings\": [{listings}]}}]}}";

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.StructuralViolation, result.Error);
        Assert.Contains("listings", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_OffsetlessFirstSeen_IsNormalizedToUtcKind() {
        // No trailing Z: System.Text.Json parses this as DateTimeKind.Unspecified,
        // so the value exercises NormalizeSnapshot's SpecifyKind path.
        var json = "{\"schemaVersion\": 1, \"listingSnapshots\": [{\"retainerId\": 42, \"observedAtUtc\": \"2026-06-01T00:00:00Z\", \"retainerGil\": 0, \"listings\": [{\"slot\": 0, \"itemId\": 100, \"quantity\": 1, \"unitPrice\": 10000, \"isHq\": false, \"firstSeenUtc\": \"2026-06-01T00:00:00\"}]}]}";

        var result = LedgerSerializer.Deserialize(json);

        Assert.Equal(LedgerLoadError.None, result.Error);
        Assert.Equal(DateTimeKind.Utc, result.Ledger!.LatestSnapshotsByRetainerId[42].Listings[0].FirstSeenUtc!.Value.Kind);
    }
}
