using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// The invariant every wide cell depends on: a two-column character occupies its own cell and the
/// spacer after it, and no operation may leave one half without the other. A renderer that meets a
/// width-2 cell with a real character in its second column draws a two-column glyph into one and
/// the rest of the row shifts.
/// </summary>
public class WideCellInvariantTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static Terminal NewTerminal(int cols = 10, int rows = 4) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static void AssertNoOrphans(Terminal t, int row = 0)
    {
        var line = t.Buffer.Lines[row]!;
        for (var x = 0; x < t.Cols; x++)
        {
            if (line[x].Width != 2)
                continue;

            Assert.True(x + 1 < t.Cols, $"wide cell at {x} has no room for its spacer");
            Assert.True(string.IsNullOrEmpty(line[x + 1].Content) || line[x + 1].Content == " ",
                $"wide cell at {x} is followed by '{line[x + 1].Content}' instead of its spacer");
        }
    }

    [Fact]
    public void Printing_over_the_first_half_of_a_wide_character_removes_the_second()
    {
        var terminal = NewTerminal();
        terminal.Write("\u6F22");             // a two-column han character
        terminal.Write($"{Esc}[1;1H");
        terminal.Write("e");

        AssertNoOrphans(terminal);
    }

    [Fact]
    public void Printing_over_the_spacer_removes_the_glyph()
    {
        var terminal = NewTerminal();
        terminal.Write("\u6F22");
        terminal.Write($"{Esc}[1;2H");
        terminal.Write("e");

        AssertNoOrphans(terminal);
    }

    [Fact]
    public void Erasing_from_the_spacer_takes_the_whole_character()
    {
        var terminal = NewTerminal();
        terminal.Write("\u6F22B");
        terminal.Write($"{Esc}[1;2H{Esc}[K");   // EL from the spacer

        AssertNoOrphans(terminal);
    }

    [Fact]
    public void Deleting_a_character_does_not_split_a_wide_neighbour()
    {
        var terminal = NewTerminal();
        terminal.Write("ab\u6F22cd");
        terminal.Write($"{Esc}[1;3H{Esc}[P");   // DCH on the wide character

        AssertNoOrphans(terminal);
    }

    [Fact]
    public void A_wide_character_at_the_last_column_wraps_instead_of_being_crammed_in()
    {
        // It needs two columns; written at the last one it was stored with no room for its spacer
        // and left the cursor past the pending-wrap position.
        var terminal = NewTerminal(cols: 10);
        terminal.Write("012345678");            // cursor at column 9, the last
        terminal.Write("\u6F22");

        AssertNoOrphans(terminal);
        Assert.Equal(1, terminal.Buffer.Y);
        Assert.Equal("\u6F22", terminal.Buffer.Lines[1]![0].Content);
    }

    [Fact]
    public void A_cluster_never_measures_more_than_two_columns()
    {
        // Two spacing marks measured 3, so the loop and the incremental path disagreed about the
        // same text and a width no cell machinery models reached the buffer.
        var terminal = NewTerminal(cols: 20);
        terminal.Write("\u0915\u094D\u0915\u094D\u0915");   // ka virama ka virama ka

        var line = terminal.Buffer.Lines[0]!;
        Assert.True(line[0].Width <= 2, $"cluster measured {line[0].Width} columns");
    }

    [Fact]
    public void A_zwj_only_keeps_the_cluster_for_a_pictograph()
    {
        // GB11 is ZWJ x Extended_Pictographic. Accepting anything meant an emoji, a ZWJ and a
        // letter put the letter inside the emoji's cell, where it could not be seen or selected.
        var terminal = NewTerminal(cols: 20);
        terminal.Write("\U0001F468\u200D" + "e");

        var line = terminal.Buffer.Lines[0]!;
        Assert.DoesNotContain("e", line[0].Content);
        Assert.Equal("e", line[2].Content);
    }

    [Fact]
    public void A_zwj_emoji_sequence_still_joins()
    {
        var terminal = NewTerminal(cols: 20);
        terminal.Write("\U0001F468\u200D\U0001F469");   // man ZWJ woman

        // Counted in codepoints rather than substring-matched: a failure message about surrogate
        // pairs is unreadable, and the question is only whether both members joined one cluster.
        var line = terminal.Buffer.Lines[0]!;
        var runes = line[0].Content.EnumerateRunes().Select(r => r.Value).ToArray();
        Assert.Equal([0x1F468, 0x200D, 0x1F469], runes);
    }

    [Fact]
    public void A_wide_character_that_cannot_fit_is_dropped_when_wrapping_is_off()
    {
        // The early wrap that makes room for a two-column character is guarded by DECAWM, so with
        // wrapping off the character reached the last column and was stored there while the margin
        // refused its spacer -- producing the very orphan this class is about, by the one path
        // that skipped the guard. xterm.js parks the cursor and drops the character instead.
        var terminal = NewTerminal(cols: 10);
        terminal.Write($"{Esc}[?7l");          // DECAWM off
        terminal.Write($"{Esc}[1;10H");        // last column
        terminal.Write("界");

        AssertNoOrphans(terminal);
        Assert.NotEqual(2, terminal.Buffer.Lines[0]![9].Width);
    }

    [Fact]
    public void A_wide_character_copied_into_a_line_is_still_repaired_afterwards()
    {
        // HasWideCells was latched only by SetCell, but cells also arrive through the bulk copies
        // that margin scrolling and reflow use, which write the array directly. A line that got
        // its wide character that way kept a false latch, so every later repair skipped it and the
        // orphan came back.
        var line = new XTerm.Buffer.BufferLine(10, XTerm.Buffer.BufferCell.Space);
        var source = new XTerm.Buffer.BufferLine(10, XTerm.Buffer.BufferCell.Space);

        var wide = XTerm.Buffer.BufferCell.Space;
        wide.Content = "界";
        wide.Width = 2;
        source.SetCell(2, ref wide);

        line.CopyCellsFrom(source, 0, 0, 10, applyInReverse: false);

        Assert.True(line.HasWideCells,
            "a line holding a copied wide cell must report it, or its repairs are skipped");

        // And the repair the latch gates actually runs: clearing the spacer's column alone would
        // leave the width-2 cell in front of it orphaned.
        line.Fill(XTerm.Buffer.BufferCell.Space, 3, 4);
        Assert.NotEqual(2, line[2].Width);
    }

    [Fact]
    public void A_cloned_line_carries_the_wide_cell_latch()
    {
        var source = new XTerm.Buffer.BufferLine(10, XTerm.Buffer.BufferCell.Space);
        var wide = XTerm.Buffer.BufferCell.Space;
        wide.Content = "界";
        wide.Width = 2;
        source.SetCell(0, ref wide);

        Assert.True(source.Clone().HasWideCells);
    }

    [Fact]
    public void A_recycled_line_takes_the_latch_of_what_replaced_it()
    {
        // CopyFrom REPLACES the contents, so the latch belongs to the incoming cells. Scrollback
        // lines are recycled, and one that once held a wide character would otherwise carry the
        // latch for the rest of the session.
        var source = new XTerm.Buffer.BufferLine(10, XTerm.Buffer.BufferCell.Space);
        var wide = XTerm.Buffer.BufferCell.Space;
        wide.Content = "界";
        wide.Width = 2;
        source.SetCell(0, ref wide);

        var recycled = new XTerm.Buffer.BufferLine(10, XTerm.Buffer.BufferCell.Space);
        recycled.CopyFrom(source);
        Assert.True(recycled.HasWideCells);

        recycled.CopyFrom(new XTerm.Buffer.BufferLine(10, XTerm.Buffer.BufferCell.Space));
        Assert.False(recycled.HasWideCells);
    }
}
