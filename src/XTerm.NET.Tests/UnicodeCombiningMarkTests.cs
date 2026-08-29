using XTerm.Options;

namespace XTerm.Tests;

/// <summary>Combining marks from ordinary scripts stay in their base character's cell.</summary>
public class UnicodeCombiningMarkTests
{
    [Theory]
    [InlineData("\u0628\u064E", "Arabic non-spacing mark")]
    [InlineData("\u05E9\u05B8", "Hebrew non-spacing mark")]
    [InlineData("\u0915\u093E", "Devanagari spacing combining mark")]
    [InlineData("\u0E01\u0E48", "Thai non-spacing mark")]
    [InlineData("\U0001E922\U0001E944", "astral Adlam non-spacing mark")]
    public void A_mark_stays_with_its_base_character(string cluster, string _)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write(cluster + "X");

        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal(cluster, line[0].Content);
        Assert.Equal("X", line[1].Content);
        Assert.Equal(2, terminal.Buffer.X);
    }
}
