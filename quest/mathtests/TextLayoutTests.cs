using System.Linq;
using SpatialMac;
using Xunit;

public class TextLayoutTests {
    [Fact] public void WrapsAtWordBoundaries() {
        Assert.Equal("the quick\nbrown fox", TextLayout.Wrap("the quick brown fox", 10, 5));
    }
    [Fact] public void NeverExceedsColumns() {
        string text = "Supercalifragilisticexpialidocious is a very long word in a rather ordinary sentence of normal words";
        foreach (int cols in new[] { 5, 8, 12, 20, 44 })
            Assert.All(TextLayout.Wrap(text, cols, 100).Split('\n'), line => Assert.True(line.Length <= cols, $"{cols}: '{line}'"));
    }
    [Fact] public void PreservesAllWordsWhenNotTruncated() {
        string text = "alpha beta gamma delta epsilon zeta eta theta";
        Assert.Equal(text, string.Join(" ", TextLayout.Wrap(text, 12, 50).Split('\n', ' ')));
    }
    [Fact] public void HonorsExplicitNewlinesAndCountsThemProperly() {
        Assert.Equal("one\ntwo", TextLayout.Wrap("one\ntwo", 40, 5));
        Assert.Equal("a\n\nb", TextLayout.Wrap("a\n\nb", 40, 5));
        Assert.Equal("a\nb", TextLayout.Wrap("a\r\nb", 40, 5));
    }
    [Fact] public void CapsLinesWithAnEllipsis() {
        string r = TextLayout.Wrap("one two three four five six seven eight nine ten", 8, 3);
        var lines = r.Split('\n');
        Assert.Equal(3, lines.Length); Assert.EndsWith("…", lines[2]);
    }
    [Fact] public void EllipsisNeverOverflowsAFullLine() {
        string r = TextLayout.Wrap("abcdefgh ijklmnop qrstuvwx yz", 8, 2);
        Assert.All(r.Split('\n'), l => Assert.True(l.Length <= 8, l));
    }
    [Fact] public void TrailingBlankLinesAreTrimmed() { Assert.Equal("hello", TextLayout.Wrap("hello\n\n\n", 20, 5)); }
    [Fact] public void DegenerateInputsReturnEmptyAndNeverHang() {
        Assert.Equal("", TextLayout.Wrap(null, 10, 3)); Assert.Equal("", TextLayout.Wrap("", 10, 3));
        Assert.Equal("", TextLayout.Wrap("text", 0, 3)); Assert.Equal("", TextLayout.Wrap("text", 10, 0));
        Assert.Equal("a\nb", TextLayout.Wrap("ab", 1, 5));
        Assert.Equal("", TextLayout.Wrap("   ", 10, 3));
    }
    [Theory]
    [InlineData("hello", 10, "hello")]
    [InlineData("hello world", 5, "hell…")]
    [InlineData("hi", 1, "…")]
    [InlineData("", 5, "")]
    [InlineData(null, 5, "")]
    [InlineData("anything", 0, "")]
    public void Truncate(string s, int max, string expected) { Assert.Equal(expected, TextLayout.Truncate(s, max)); }
}
