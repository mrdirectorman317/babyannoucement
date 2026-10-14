using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SpatialMac;
using Xunit;

public class RoomBookTests {
    static readonly DateTime T0 = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    static string Anchor() { return Guid.NewGuid().ToString(); }
    static SourceKey Safari(string title = "Apple") { return new SourceKey("window", "Safari", title); }
    static PoseData Pose(float x = 1, float y = 0.5f, float z = 2) { return new PoseData(x, y, z, 0, 0, 0, 1); }
    static Room NewRoom(RoomBook b, string name = "Kitchen") { string e; var r = b.Create(name, Anchor(), T0, out e); Assert.Null(e); return r; }

    // ---- names ---------------------------------------------------------------------------------------------------
    [Theory]
    [InlineData("kitchen", "Kitchen")]
    [InlineData("  living   room ", "Living Room")]
    [InlineData("Mom's Place", "Mom's Place")]
    [InlineData("my_office.2", "My Office 2")]
    [InlineData("iPhone Lab", "iPhone Lab")]       // already has capitals: left alone
    public void NamesAreCleanedAndDictatedNamesTitleCased(string raw, string expected) {
        Assert.Equal(expected, RoomBook.SanitizeName(raw));
    }
    [Fact] public void HostileNamesLoseControlAndBidiCharacters() {
        Assert.Equal("Kitchen", RoomBook.SanitizeName("Kitchen‮\u0007\n​"));
        Assert.Equal("", RoomBook.SanitizeName("<>$%^*()"));
        Assert.Equal("", RoomBook.SanitizeName("   "));
        Assert.Equal("", RoomBook.SanitizeName(null));
        Assert.Equal("Hi", RoomBook.SanitizeName("Hi 😀"));
    }
    [Fact] public void NamesAreCapped() {
        string n = RoomBook.SanitizeName(new string('a', 100));
        Assert.True(n.Length <= RoomBook.MaxNameLength);
    }
    [Fact] public void DuplicateNamesGetNumbers() {
        var b = new RoomBook();
        Assert.Equal("Kitchen", NewRoom(b, "kitchen").name);
        Assert.Equal("KITCHEN 2", NewRoom(b, "KITCHEN").name);        // typed case is kept; only dictated all-lowercase names are title-cased
        Assert.Equal("Kitchen 3", NewRoom(b, "Kitchen").name);
    }
    [Fact] public void LongDuplicateNamesStayWithinTheLimit() {
        var b = new RoomBook();
        string longName = new string('x', 40);
        var a = NewRoom(b, longName); var c = NewRoom(b, longName);
        Assert.True(a.name.Length <= RoomBook.MaxNameLength); Assert.True(c.name.Length <= RoomBook.MaxNameLength);
        Assert.NotEqual(a.name, c.name);
    }
    [Fact] public void BlankNameBecomesRoomN() {
        var b = new RoomBook();
        Assert.Equal("Room 1", NewRoom(b, "").name);
        Assert.Equal("Room 2", NewRoom(b, null).name);
    }
    [Fact] public void RenameKeepsNamesUniqueButAllowsKeepingOwnName() {
        var b = new RoomBook(); var k = NewRoom(b, "Kitchen"); var o = NewRoom(b, "Office"); string e;
        Assert.True(b.Rename(k, "kitchen", out e));          // own name, different case
        Assert.Equal("Kitchen", k.name);
        Assert.True(b.Rename(o, "Kitchen", out e));          // collides with another room -> numbered
        Assert.Equal("Kitchen 2", o.name);
        Assert.False(b.Rename(k, "!!!", out e)); Assert.NotNull(e);
        Assert.Equal("Kitchen", k.name);
        Assert.False(b.Rename(new Room(), "X", out e));      // not in the book
    }
    [Fact] public void FindByNameExactThenUniquePrefixNeverAmbiguous() {
        var b = new RoomBook(); var kitchen = NewRoom(b, "Kitchen"); var kids = NewRoom(b, "Kids Room"); var office = NewRoom(b, "Office");
        Assert.Same(kitchen, b.FindByName("kitchen"));
        Assert.Same(office, b.FindByName("off"));
        Assert.Null(b.FindByName("ki"));                      // Kitchen and Kids Room: ambiguous -> nothing
        Assert.Same(kids, b.FindByName("kids"));
        Assert.Null(b.FindByName("garage")); Assert.Null(b.FindByName("")); Assert.Null(b.FindByName(null));
    }

    // ---- creating / limits --------------------------------------------------------------------------------------
    [Fact] public void CreateMakesTheRoomActive() {
        var b = new RoomBook(); var r = NewRoom(b);
        Assert.Same(r, b.Active); Assert.Equal(T0.Ticks, r.lastUsedUtcTicks);
    }
    [Fact] public void RoomLimitIsEnforced() {
        var b = new RoomBook(); string e;
        for (int i = 0; i < RoomBook.MaxRooms; i++) NewRoom(b, "R" + i);
        Assert.Null(b.Create("one more", Anchor(), T0, out e)); Assert.Contains("limit", e);
        Assert.Equal(RoomBook.MaxRooms, b.Rooms.Count);
    }
    [Fact] public void AnchorsMustBeValidAndUniquePerRoom() {
        var b = new RoomBook(); string e; string a = Anchor();
        Assert.Null(b.Create("Bad", "not-a-guid", T0, out e)); Assert.NotNull(e);
        Assert.NotNull(b.Create("Good", a, T0, out e));
        Assert.Null(b.Create("Same anchor", a, T0, out e)); Assert.NotNull(e);
    }
    [Fact] public void DeleteQueuesTheAnchorForErasureAndClearsActive() {
        var b = new RoomBook(); string e; string a = Anchor();
        var r = b.Create("Kitchen", a, T0, out e);
        Assert.True(b.Delete(r));
        Assert.Empty(b.Rooms); Assert.Equal("", b.Data.activeRoomId); Assert.Null(b.Active);
        Assert.Equal(new[] { a }, b.Data.pendingErase);
        b.MarkErased(a); Assert.Empty(b.Data.pendingErase);
        Assert.False(b.Delete(r));                            // already gone
    }
    [Fact] public void DeletingARoomWithoutAnAnchorQueuesNothing() {
        var b = new RoomBook(); string e; var r = b.Create("Temp", "", T0, out e);
        Assert.True(b.Delete(r)); Assert.Empty(b.Data.pendingErase);
    }
    [Fact] public void NextCyclesThroughRoomsAndWraps() {
        var b = new RoomBook(); var a = NewRoom(b, "A"); var c = NewRoom(b, "B"); var d = NewRoom(b, "C");
        Assert.Same(a, b.Next(null)); Assert.Same(c, b.Next(a)); Assert.Same(d, b.Next(c)); Assert.Same(a, b.Next(d));
        Assert.Null(new RoomBook().Next(null));
    }
    [Fact] public void SetActiveTouchesRecency() {
        var b = new RoomBook(); var a = NewRoom(b, "A"); var c = NewRoom(b, "B");
        Assert.True(b.SetActive(a, T0.AddHours(1)));
        Assert.Same(a, b.Active); Assert.Equal(T0.AddHours(1).Ticks, a.lastUsedUtcTicks);
        Assert.False(b.SetActive(new Room(), T0));
        Assert.Same(a, b.ByRecency().First());
    }

    // ---- which room am I in? -----------------------------------------------------------------------------------
    [Fact] public void BestRoomPicksTheMostRecentRoomWhoseAnchorLocalized() {
        var b = new RoomBook(); string e; string a1 = Anchor(), a2 = Anchor(), a3 = Anchor();
        var r1 = b.Create("One", a1, T0, out e); var r2 = b.Create("Two", a2, T0.AddMinutes(5), out e); var r3 = b.Create("Three", a3, T0.AddMinutes(9), out e);
        Assert.Same(r3, b.BestRoom(new[] { a1, a2, a3 }));
        Assert.Same(r2, b.BestRoom(new[] { a1, a2 }));
        Assert.Same(r1, b.BestRoom(new[] { a1.ToUpperInvariant() }));
        Assert.Null(b.BestRoom(new[] { Anchor() })); Assert.Null(b.BestRoom(null)); Assert.Null(b.BestRoom(new string[0]));
    }
    [Fact] public void RoomsWithoutAnchorsAreNeverDetected() {
        var b = new RoomBook(); string e; b.Create("NoAnchor", "", T0, out e);
        Assert.Null(b.BestRoom(new[] { "" }));
    }

    // ---- pinning windows ---------------------------------------------------------------------------------------
    [Fact] public void CannotPinWithoutARoomOrAnAnchor() {
        var b = new RoomBook(); string e;
        Assert.False(b.Pin(null, Safari(), Pose(), 0.9f, out e)); Assert.Contains("new room", e);
        var noAnchor = b.Create("Temp", "", T0, out e);
        Assert.False(b.Pin(noAnchor, Safari(), Pose(), 0.9f, out e)); Assert.Contains("anchor", e);
        Assert.Empty(noAnchor.panels);
    }
    [Fact] public void PinStoresANormalizedRecord() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        Assert.True(b.Pin(r, Safari(), new PoseData(1, 2, 3, 0, 0, 0, 1.2f), 1.1f, out e));   // slightly un-normalized: accepted and fixed
        var rec = r.panels.Single();
        Assert.Equal(1f, rec.pose.rw, 5);                     // quaternion normalized
        Assert.Equal(1.1f, rec.width);
    }
    [Fact] public void PinningTheSameWindowAgainReplacesItsPlace() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        Assert.True(b.Pin(r, Safari(), Pose(1, 1, 1), 0.9f, out e));
        Assert.True(b.Pin(r, Safari(), Pose(5, 5, 5), 1.5f, out e));
        Assert.Single(r.panels); Assert.Equal(5f, r.panels[0].pose.px); Assert.Equal(1.5f, r.panels[0].width);
    }
    [Fact] public void DifferentTitlesAreDifferentWindows() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        b.Pin(r, Safari("A"), Pose(), 0.9f, out e); b.Pin(r, Safari("B"), Pose(), 0.9f, out e);
        Assert.Equal(2, r.panels.Count);
    }
    [Fact] public void PerRoomPinLimitStillAllowsReplacingExistingPins() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        for (int i = 0; i < RoomBook.MaxPanelsPerRoom; i++) Assert.True(b.Pin(r, Safari("W" + i), Pose(), 0.9f, out e));
        Assert.False(b.Pin(r, Safari("overflow"), Pose(), 0.9f, out e)); Assert.Contains("already has", e);
        Assert.True(b.Pin(r, Safari("W3"), Pose(9, 9, 9), 0.9f, out e));          // re-pin existing: fine
        Assert.Equal(RoomBook.MaxPanelsPerRoom, r.panels.Count);
    }
    [Theory]
    [InlineData(float.NaN, 0f, 0f, 0.9f)]
    [InlineData(float.PositiveInfinity, 0f, 0f, 0.9f)]
    [InlineData(5000f, 0f, 0f, 0.9f)]
    [InlineData(0f, 0f, 0f, 0.1f)]
    [InlineData(0f, 0f, 0f, 9f)]
    [InlineData(0f, 0f, 0f, float.NaN)]
    public void InvalidPosesAndSizesAreRejected(float x, float y, float z, float width) {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        Assert.False(b.Pin(r, Safari(), new PoseData(x, y, z, 0, 0, 0, 1), width, out e)); Assert.NotNull(e);
        Assert.Empty(r.panels);
    }
    [Fact] public void NonRotationsAreRejected() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        Assert.False(b.Pin(r, Safari(), new PoseData(0, 0, 0, 0, 0, 0, 0), 0.9f, out e));
        Assert.False(b.Pin(r, Safari(), new PoseData(0, 0, 0, 9, 9, 9, 9), 0.9f, out e));
        Assert.False(b.Pin(r, Safari(), new PoseData(0, 0, 0, 0, 0, 0, 2f), 0.9f, out e));     // far from unit length
    }
    [Fact] public void EmptySourceIsRejectedAndLongTitlesAreClipped() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        Assert.False(b.Pin(r, new SourceKey("", "", ""), Pose(), 0.9f, out e));
        Assert.False(b.Pin(r, null, Pose(), 0.9f, out e));
        Assert.True(b.Pin(r, new SourceKey("window", "Notes", new string('t', 500)), Pose(), 0.9f, out e));
        Assert.Equal(RoomBook.MaxTextLength, r.panels.Single().source.title.Length);
    }
    [Fact] public void UnpinRemovesOnlyThatWindow() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        b.Pin(r, Safari("A"), Pose(), 0.9f, out e); b.Pin(r, Safari("B"), Pose(), 0.9f, out e);
        Assert.True(b.Unpin(r, Safari("A")));
        Assert.Equal("B", r.panels.Single().source.title);
        Assert.False(b.Unpin(r, Safari("A"))); Assert.False(b.Unpin(null, Safari())); Assert.False(b.Unpin(r, null));
    }
    [Fact] public void WindowsPinnedInDifferentRoomsDoNotInterfere() {
        var b = new RoomBook(); var k = NewRoom(b, "Kitchen"); var o = NewRoom(b, "Office"); string e;
        b.Pin(k, Safari(), Pose(1, 1, 1), 0.9f, out e); b.Pin(o, Safari(), Pose(7, 7, 7), 1.2f, out e);
        Assert.Equal(1f, b.FindPinned(k, Safari()).pose.px); Assert.Equal(7f, b.FindPinned(o, Safari()).pose.px);
        b.Unpin(k, Safari());
        Assert.Null(b.FindPinned(k, Safari())); Assert.NotNull(b.FindPinned(o, Safari()));
    }

    // ---- restoring ---------------------------------------------------------------------------------------------
    [Fact] public void RestoreMatchesOnlyUnambiguousExactWindows() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        b.Pin(r, Safari("Docs"), Pose(), 0.9f, out e);
        b.Pin(r, new SourceKey("window", "Notes", "Todo"), Pose(), 0.9f, out e);
        b.Pin(r, new SourceKey("window", "Mail", "Inbox"), Pose(), 0.9f, out e);
        b.Pin(r, new SourceKey("display", "Desktop", "Display 1"), Pose(), 0.9f, out e);
        var available = new List<SourceKey> {
            Safari("Docs"), new SourceKey("window", "Notes", "Todo"), new SourceKey("window", "Notes", "Todo"),   // Notes appears twice: ambiguous
            new SourceKey("window", "Safari", "Different title")                                                   // Mail and the display are not open
        };
        var plan = b.PlanRestore(r, available);
        Assert.Equal(new[] { 0 }, plan.MatchedIndex);
        Assert.Equal("Safari", plan.Matched.Single().source.app);
        Assert.Equal("Notes", plan.Ambiguous.Single().source.app);
        Assert.Equal(new[] { "Mail", "Desktop" }.OrderBy(x => x), plan.Missing.Select(m => m.source.app).OrderBy(x => x));
    }
    [Fact] public void RestoreNeverMatchesByAppAloneOrByLooseTitle() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        b.Pin(r, Safari("Private bank page"), Pose(), 0.9f, out e);
        var plan = b.PlanRestore(r, new List<SourceKey> { Safari("Recipe blog") });
        Assert.Empty(plan.Matched); Assert.Single(plan.Missing);
    }
    [Fact] public void RestoreHandlesNullsAndEmpty() {
        var b = new RoomBook(); var r = NewRoom(b);
        Assert.Empty(b.PlanRestore(null, null).Matched);
        Assert.Empty(b.PlanRestore(r, null).Matched);
    }
    [Fact] public void RestoreSkipsCorruptRecords() {
        var b = new RoomBook(); var r = NewRoom(b);
        r.panels.Add(new PanelRecord { source = Safari(), pose = new PoseData(float.NaN, 0, 0, 0, 0, 0, 1), width = 0.9f });
        r.panels.Add(new PanelRecord { source = Safari("ok"), pose = Pose(), width = 0.9f });
        var plan = b.PlanRestore(r, new List<SourceKey> { Safari(), Safari("ok") });
        Assert.Equal(1, plan.Invalid); Assert.Single(plan.Matched);
    }

    // ---- the data-loss bug: saved rooms must not depend on which windows happen to be open ---------------------------
    [Fact] public void SavedRoomsSurviveASessionWhereNothingWasOpened() {
        var b = new RoomBook(); var r = NewRoom(b); string e;
        b.Pin(r, Safari(), Pose(), 0.9f, out e);
        string json = JsonSerializer.Serialize(b.Data, new JsonSerializerOptions { IncludeFields = true });
        // "launch, open nothing, quit": the app saves the book as-is (no panels derived from what is open)
        var loaded = new RoomBook(JsonSerializer.Deserialize<RoomBookData>(json, new JsonSerializerOptions { IncludeFields = true }));
        string json2 = JsonSerializer.Serialize(loaded.Data, new JsonSerializerOptions { IncludeFields = true });
        var again = new RoomBook(JsonSerializer.Deserialize<RoomBookData>(json2, new JsonSerializerOptions { IncludeFields = true }));
        Assert.Single(again.Rooms); Assert.Single(again.Rooms[0].panels);
    }

    // ---- persistence shape + repair ----------------------------------------------------------------------------
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    [Fact] public void RoundTripsThroughJson() {
        var b = new RoomBook(); string e; string a = Anchor();
        var r = b.Create("Kitchen", a, T0, out e);
        b.Pin(r, Safari("Docs"), new PoseData(1, 2, 3, 0, 0.7071f, 0, 0.7071f), 1.2f, out e);
        b.Delete(b.Create("Temp", Anchor(), T0, out e));
        var back = new RoomBook(JsonSerializer.Deserialize<RoomBookData>(JsonSerializer.Serialize(b.Data, Opt), Opt));
        Assert.Equal(0, back.Repair());
        Assert.Equal("Kitchen", back.Rooms.Single().name); Assert.Equal(a, back.Rooms[0].anchorId);
        Assert.Equal(1.2f, back.Rooms[0].panels.Single().width); Assert.Single(back.Data.pendingErase);
    }
    [Fact] public void RepairDropsDuplicatesBadAnchorsAndInvalidRecords() {
        string good = Anchor();
        var data = new RoomBookData { activeRoomId = "ghost" };
        data.rooms.Add(new Room { id = "1", name = "kitchen", anchorId = good, panels = { new PanelRecord { source = Safari(), pose = Pose(), width = 0.9f }, new PanelRecord { source = Safari(), pose = Pose(), width = 0.9f }, new PanelRecord { source = Safari("bad"), pose = Pose(), width = 99 } } });
        data.rooms.Add(new Room { id = "1", name = "dup id", anchorId = Anchor() });
        data.rooms.Add(new Room { id = "2", name = "Kitchen", anchorId = good, panels = { new PanelRecord { source = Safari("p"), pose = Pose(), width = 0.9f } } });   // duplicate anchor
        data.rooms.Add(new Room { id = "3", name = "‮\u0007", anchorId = "garbage" });
        data.rooms.Add(null);
        data.pendingErase.AddRange(new[] { good, "junk", Anchor(), null });
        var b = new RoomBook(data);
        Assert.True(b.Repair() > 0);
        Assert.Equal(3, b.Rooms.Count);                                   // dup id and null dropped
        Assert.Equal("Kitchen", b.Rooms[0].name); Assert.Single(b.Rooms[0].panels);   // dup + bad-width records gone
        Assert.Equal("", b.Rooms[1].anchorId); Assert.Empty(b.Rooms[1].panels);   // second user of the anchor loses it, and its pins
        Assert.NotEqual(b.Rooms[0].name, b.Rooms[1].name);                // names made unique
        Assert.False(string.IsNullOrWhiteSpace(b.Rooms[2].name));        // unusable name replaced
        Assert.Equal("", b.Data.activeRoomId);                            // pointed at a room that doesn't exist
        Assert.Single(b.Data.pendingErase);                               // only the valid, unused one
        Assert.DoesNotContain(good, b.Data.pendingErase);
    }
    [Fact] public void RepairEnforcesLimitsOnOversizedFiles() {
        var data = new RoomBookData();
        for (int i = 0; i < 40; i++) data.rooms.Add(new Room { id = "r" + i, name = "Room " + i, anchorId = Anchor() });
        var heavy = data.rooms[0];
        for (int i = 0; i < 30; i++) heavy.panels.Add(new PanelRecord { source = Safari("w" + i), pose = Pose(), width = 0.9f });
        for (int i = 0; i < 100; i++) data.pendingErase.Add(Anchor());
        var b = new RoomBook(data); b.Repair();
        Assert.Equal(RoomBook.MaxRooms, b.Rooms.Count);
        Assert.Equal(RoomBook.MaxPanelsPerRoom, b.Rooms[0].panels.Count);
        Assert.True(b.Data.pendingErase.Count <= RoomBook.MaxPending);
    }
    [Fact] public void RepairToleratesMissingCollections() {
        var data = new RoomBookData { rooms = null, pendingErase = null };
        var b = new RoomBook(data); b.Repair();
        Assert.NotNull(b.Data.rooms); Assert.NotNull(b.Data.pendingErase);
    }
    [Fact] public void PartialJsonLoadsWithDefaults() {
        var d = JsonSerializer.Deserialize<RoomBookData>("{\"rooms\":[{\"id\":\"x\",\"name\":\"Lab\"}]}", Opt);
        var b = new RoomBook(d); b.Repair();
        Assert.Equal("Lab", b.Rooms.Single().name); Assert.Equal("", b.Rooms[0].anchorId); Assert.Equal(1, d.version);
    }
}
