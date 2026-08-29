using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Conjunct stacking beyond UAX #29's InCB eight: wcwidth's virama + invisible-stacker set
/// covers Khmer's coeng, Tai Tham's sakot, Javanese's pangkon, Myanmar's stacker and the SMP
/// Brahmic scripts. A stacked syllable is one cluster of two columns; a DEAD consonant --
/// linker with nothing following -- stays one, even where the linker's category is Mc
/// (Javanese pangkon, Grantha virama), because a killer is not a vowel.
/// </summary>
public class ConjunctStackingTests
{
    private static Terminal Write(string text)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 3 });
        terminal.Write(text);
        return terminal;
    }

    [Theory]
    [InlineData("\u1780\u17D2\u178A\u17C5", "Khmer coeng stack with vowel")]
    [InlineData("\u1012\u1039\u1002\u1031", "Myanmar stacker with vowel")]
    [InlineData("\uA98F\uA9C0\uA98F", "Javanese pangkon stack")]
    [InlineData("\u1A2F\u1A60\u1A45\u1A60\u1A3F\u1A62", "Tai Tham double sakot")]
    [InlineData("\U00011107\U00011133\U00011120\U0001112C", "Chakma stack, astral")]
    public void A_stacked_syllable_is_one_cluster_of_two_columns(string cluster, string _)
    {
        var terminal = Write(cluster + "X");
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(cluster, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal("X", line[2].Content);
        Assert.Equal(3, terminal.Buffer.X);
    }

    [Theory]
    [InlineData("\uA98F\uA9C0", "Javanese dead consonant: pangkon is Mc but kills, not vowels")]
    [InlineData("\U00011315\U0001134D", "Grantha dead consonant, astral, virama is Mc")]
    public void A_dead_consonant_stays_one_column(string cluster, string _)
    {
        var terminal = Write(cluster + "X");
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(cluster, line[0].Content);
        Assert.Equal(1, line[0].Width);
        Assert.Equal("X", line[1].Content);
        Assert.Equal(2, terminal.Buffer.X);
    }
}
