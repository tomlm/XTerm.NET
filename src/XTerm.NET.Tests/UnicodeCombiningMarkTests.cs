using XTerm.Options;

namespace XTerm.Tests;

/// <summary>Combining marks from ordinary scripts stay in their base character's cell.</summary>
public class UnicodeCombiningMarkTests
{
    [Theory]
    [InlineData("\u0628\u064E", 1, "Arabic non-spacing mark")]
    [InlineData("\u05E9\u05B8", 1, "Hebrew non-spacing mark")]
    [InlineData("\u0915\u093E", 2, "Devanagari spacing combining mark: a matra takes its column")]
    [InlineData("\u0E01\u0E48", 1, "Thai non-spacing mark")]
    [InlineData("\U0001E922\U0001E944", 1, "astral Adlam non-spacing mark")]
    public void A_mark_stays_with_its_base_character(string cluster, int width, string _)
    {
        // One cell either way; the WIDTH depends on the mark's kind. Non-spacing marks add no
        // column. SPACING combining marks add one, as wcwidth has always said -- every
        // wcwidth-consuming application lays out on that arithmetic, so the cluster's cell
        // widens rather than the cursor standing still.
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write(cluster + "X");

        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal(cluster, line[0].Content);
        Assert.Equal(width, line[0].Width);
        Assert.Equal("X", line[width].Content);
        Assert.Equal(width + 1, terminal.Buffer.X);
    }

    // ---- the sequence rules: GB6-GB8 and GB9c, which no per-codepoint category can express ----

    [Fact]
    public void Decomposed_hangul_composes_into_one_cell()
    {
        // macOS filesystems store names in NFD, so a Korean directory listing arrives exactly
        // like this: L V T sequences. "한" decomposed is U+1112 U+1161 U+11AB — one cell, width
        // two, like its precomposed twin.
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write("\u1112\u1161\u11ABX");

        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal("\u1112\u1161\u11AB", line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal("X", line[2].Content);
        Assert.Equal(3, terminal.Buffer.X);
    }

    [Fact]
    public void A_trailing_jamo_joins_a_precomposed_LV_syllable()
    {
        // GB7: U+AC00 (가, an LV syllable) followed by U+11A8 (trailing ᆨ) is 각 in one cell.
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write("\uAC00\u11A8X");

        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal("\uAC00\u11A8", line[0].Content);
        Assert.Equal("X", line[2].Content);
    }

    [Fact]
    public void Complete_syllables_do_not_merge_into_each_other()
    {
        // Ordinary NFC Korean: LVT followed by LV starts a NEW cluster — GB6 only joins after a
        // dangling L. Two syllables, two cells.
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write("\uD55C\uAD6D");   // 한국

        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal("\uD55C", line[0].Content);
        Assert.Equal("\uAD6D", line[2].Content);
        Assert.Equal(4, terminal.Buffer.X);
    }

    [Fact]
    public void A_vowel_jamo_does_not_join_text_that_cannot_take_it()
    {
        // V after a Latin letter is a lone jamo: its own cell, not an accent on the letter.
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write("a\u1161");

        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal("a", line[0].Content);
        Assert.Equal("\u1161", line[1].Content);
    }

    [Fact]
    public void A_devanagari_conjunct_stays_in_one_cell()
    {
        // GB9c: क + ् (virama, the InCB linker) + ष — the conjunct क्ष, one cluster, one cell.
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write("\u0915\u094D\u0937X");

        // One cluster, one cell -- TWO columns, because wcwidth arithmetic for the sequence is
        // 1+0+1 and legacy applications lay out on exactly that sum.
        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal("\u0915\u094D\u0937", line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal("X", line[2].Content);
        Assert.Equal(3, terminal.Buffer.X);
    }

    [Fact]
    public void The_explicit_conjunct_form_with_ZWJ_joins_too()
    {
        // Linker then ZWJ then consonant — the explicit-conjunct request.
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write("\u0915\u094D\u200D\u0937X");

        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal("\u0915\u094D\u200D\u0937", line[0].Content);
        // Two columns like the implicit form: the ZWJ requests the conjunct GLYPH, not a
        // different width -- wcwidth arithmetic gives every letter its column either way.
        Assert.Equal(2, line[0].Width);
        Assert.Equal("X", line[2].Content);
    }

    [Fact]
    public void A_consonant_without_a_linker_before_it_starts_its_own_cluster()
    {
        // क then ष with no virama: two aksharas, two cells — GB9c requires the linker.
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });

        terminal.Write("\u0915\u0937");

        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal("\u0915", line[0].Content);
        Assert.Equal("\u0937", line[1].Content);
        Assert.Equal(2, terminal.Buffer.X);
    }
}
