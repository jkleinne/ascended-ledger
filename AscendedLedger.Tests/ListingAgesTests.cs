using System;
using AscendedLedger;
using Xunit;

public class ListingAgesTests {
    private const ulong RetainerId = 42UL;
    private static readonly DateTime T0 = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc);

    private static Listing ListingAt(int slot, long unitPrice = 10_000, DateTime? firstSeen = null) =>
        new(slot, 100, 1, unitPrice, false, firstSeen);

    [Fact]
    public void CarryForward_NullPrevious_StampsEverythingWithCurrentObservation() {
        var current = new ListingSnapshot(RetainerId, T1, 0, new[] { ListingAt(0) });

        var stamped = ListingAges.CarryForward(null, current);

        Assert.Equal(T1, Assert.Single(stamped.Listings).FirstSeenUtc);
    }

    [Fact]
    public void CarryForward_MatchedListing_InheritsFirstSeenAcrossSlotMoves() {
        var previous = new ListingSnapshot(RetainerId, T1, 0, new[] { ListingAt(0, firstSeen: T0) });
        var current = new ListingSnapshot(RetainerId, T2, 0, new[] { ListingAt(3) });

        var stamped = ListingAges.CarryForward(previous, current);

        Assert.Equal(T0, Assert.Single(stamped.Listings).FirstSeenUtc);
    }

    [Fact]
    public void CarryForward_LegacyPreviousWithoutFirstSeen_BackfillsFromPreviousObservation() {
        var previous = new ListingSnapshot(RetainerId, T1, 0, new[] { ListingAt(0) });
        var current = new ListingSnapshot(RetainerId, T2, 0, new[] { ListingAt(0) });

        var stamped = ListingAges.CarryForward(previous, current);

        Assert.Equal(T1, Assert.Single(stamped.Listings).FirstSeenUtc);
    }

    [Fact]
    public void CarryForward_Reprice_ResetsFirstSeen() {
        var previous = new ListingSnapshot(RetainerId, T1, 0, new[] { ListingAt(0, unitPrice: 10_000, firstSeen: T0) });
        var current = new ListingSnapshot(RetainerId, T2, 0, new[] { ListingAt(0, unitPrice: 9_000) });

        var stamped = ListingAges.CarryForward(previous, current);

        Assert.Equal(T2, Assert.Single(stamped.Listings).FirstSeenUtc);
    }

    [Fact]
    public void CarryForward_DuplicateListings_PairFifoSoOldestAgesSurvive() {
        var previous = new ListingSnapshot(RetainerId, T1, 0, new[] { ListingAt(0, firstSeen: T0), ListingAt(1, firstSeen: T1) });
        var current = new ListingSnapshot(RetainerId, T2, 0, new[] { ListingAt(5), ListingAt(6) });

        var stamped = ListingAges.CarryForward(previous, current);

        Assert.Equal(T0, stamped.Listings[0].FirstSeenUtc);
        Assert.Equal(T1, stamped.Listings[1].FirstSeenUtc);
    }

    [Fact]
    public void CarryForward_MoreDuplicatesThanBefore_NewCopiesStartFresh() {
        var previous = new ListingSnapshot(RetainerId, T1, 0, new[] { ListingAt(0, firstSeen: T0) });
        var current = new ListingSnapshot(RetainerId, T2, 0, new[] { ListingAt(0), ListingAt(1) });

        var stamped = ListingAges.CarryForward(previous, current);

        Assert.Equal(T0, stamped.Listings[0].FirstSeenUtc);
        Assert.Equal(T2, stamped.Listings[1].FirstSeenUtc);
    }

    [Fact]
    public void CarryForward_RetainerMismatch_Throws() {
        var previous = new ListingSnapshot(1, T1, 0, Array.Empty<Listing>());
        var current = new ListingSnapshot(2, T2, 0, Array.Empty<Listing>());

        Assert.Throws<ArgumentException>(() => ListingAges.CarryForward(previous, current));
    }
}
