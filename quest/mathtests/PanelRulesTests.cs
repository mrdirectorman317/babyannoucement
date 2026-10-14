using SpatialMac;
using Xunit;

public class PanelRulesTests {
    [Theory]
    [InlineData(PlacementMode.World, true)]
    [InlineData(PlacementMode.Follow, true)]
    [InlineData(PlacementMode.SessionLocked, false)]
    [InlineData(PlacementMode.PersistentLocked, false)]
    public void OnlyUnlockedWindowsMayBeMovedResizedBroughtOrRecentered(PlacementMode m, bool allowed) {
        Assert.Equal(allowed, PanelRules.CanMove(m));
        Assert.Equal(allowed, PanelRules.CanResize(m));
        Assert.Equal(allowed, PanelRules.CanBringHere(m));
        Assert.Equal(allowed, PanelRules.CanRecenter(m));
        Assert.Equal(!allowed, PanelRules.IsLocked(m));
    }
    [Fact] public void OnlyPersistentLockIsRoomBound() {
        foreach (PlacementMode m in System.Enum.GetValues(typeof(PlacementMode)))
            Assert.Equal(m == PlacementMode.PersistentLocked, PanelRules.IsRoomBound(m));
    }
    [Fact] public void LeavingAPinMustDropTheSavedRecord() {
        Assert.True(PanelRules.LeavingRoomBinding(PlacementMode.PersistentLocked, PlacementMode.World));
        Assert.True(PanelRules.LeavingRoomBinding(PlacementMode.PersistentLocked, PlacementMode.Follow));
        Assert.True(PanelRules.LeavingRoomBinding(PlacementMode.PersistentLocked, PlacementMode.SessionLocked));
        Assert.False(PanelRules.LeavingRoomBinding(PlacementMode.PersistentLocked, PlacementMode.PersistentLocked));
        Assert.False(PanelRules.LeavingRoomBinding(PlacementMode.World, PlacementMode.PersistentLocked));
        Assert.False(PanelRules.LeavingRoomBinding(PlacementMode.World, PlacementMode.Follow));
    }
    [Fact] public void LockedMessagesAreNonEmptyOnlyForLockedModes() {
        Assert.NotEmpty(PanelRules.LockedMessage(PlacementMode.SessionLocked));
        Assert.Contains("room", PanelRules.LockedMessage(PlacementMode.PersistentLocked));
        Assert.Empty(PanelRules.LockedMessage(PlacementMode.World));
    }
}
