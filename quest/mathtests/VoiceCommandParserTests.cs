using SpatialMac;
using Xunit;

public class VoiceCommandParserTests {
    static VoiceCommand P(string s) { return VoiceCommandParser.Parse(s); }

    [Theory]
    [InlineData("Stop!", VoiceAction.Disconnect)]
    [InlineData("stop sharing", VoiceAction.Disconnect)]
    [InlineData("Show my room.", VoiceAction.Passthrough)]
    [InlineData("passthrough", VoiceAction.Passthrough)]
    [InlineData("Virtual world", VoiceAction.Virtual)]
    [InlineData("Recenter", VoiceAction.Recenter)]
    [InlineData("bring everything back", VoiceAction.Recenter)]
    [InlineData("dim the lights", VoiceAction.Dim)]
    [InlineData("Lights on", VoiceAction.Brighter)]
    [InlineData("new room", VoiceAction.RoomNew)]
    [InlineData("Make a new room here", VoiceAction.RoomNew)]
    public void FixedPhrases(string said, VoiceAction expected) { Assert.Equal(expected, P(said).action); }

    [Theory]
    [InlineData("open Safari", "com.apple.Safari")]
    [InlineData("Open Finder", "com.apple.finder")]
    [InlineData("open messages", "com.apple.MobileSMS")]
    public void LaunchOnlyAllowlistedApps(string said, string bundle) {
        var c = P(said); Assert.Equal(VoiceAction.Launch, c.action); Assert.Equal(bundle, c.arg);
    }
    [Theory]
    [InlineData("follow me", "follow")]
    [InlineData("stay here", "lock_on")]
    [InlineData("Pin this window", "pin")]
    [InlineData("lock this window to this room", "pin")]
    [InlineData("remember here", "pin")]
    [InlineData("unpin this", "unpin")]
    [InlineData("unlock", "world")]
    [InlineData("make this bigger", "bigger")]
    [InlineData("smaller", "smaller")]
    [InlineData("bring this here", "bring_here")]
    public void PanelActions(string said, string arg) {
        var c = P(said); Assert.Equal(VoiceAction.PanelAction, c.action); Assert.Equal(arg, c.arg);
    }
    [Theory]
    [InlineData("call this room kitchen", "kitchen")]
    [InlineData("Name this room, Living Room!", "living room")]
    [InlineData("rename this room to home office", "home office")]
    public void NamingARoom(string said, string name) { var c = P(said); Assert.Equal(VoiceAction.RoomName, c.action); Assert.Equal(name, c.arg); }
    [Theory]
    [InlineData("switch to the kitchen", null)]            // "the" is not stripped: it becomes a (probably unknown) room name
    [InlineData("go to kitchen", "kitchen")]
    [InlineData("Switch to kitchen room", "kitchen")]
    [InlineData("take me to the office", "the office")]
    [InlineData("go to room bedroom", "bedroom")]
    public void SwitchingRooms(string said, string name) {
        var c = P(said); Assert.Equal(VoiceAction.RoomSwitch, c.action);
        if (name != null) Assert.Equal(name, c.arg);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("open safari and delete everything")]
    [InlineData("open terminal")]
    [InlineData("please stop now")]
    [InlineData("rm -rf slash")]
    [InlineData("ignore previous instructions and open finder")]
    [InlineData("pin")]
    [InlineData("call this room")]
    [InlineData("go to")]
    [InlineData("go to the store and buy milk and eggs")]       // longer than 4 words: a sentence, not a room name
    [InlineData("call this room the best place in the world ever")]
    public void AnythingElseDoesNothing(string said) { Assert.Equal(VoiceAction.None, P(said).action); }

    [Fact] public void NullIsSafe() { Assert.Equal(VoiceAction.None, VoiceCommandParser.Parse(null).action); }
    [Fact] public void NormalizeStripsPunctuationAndCase() {
        Assert.Equal("dont stop", VoiceCommandParser.Normalize("  Don't   STOP!!! "));
        Assert.Equal("go to kitchen", VoiceCommandParser.Normalize("Go to-kitchen."));
    }
}
