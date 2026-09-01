using XTerm.Buffer;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>DECCRA, DECFRA, DECERA -- the rectangle operations, and their shared coordinate rules.</summary>
public class RectangleOpsTests
{
    private const string Esc = "";

    private static Terminal NewTerminal(int cols = 10, int rows = 6) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static string Row(Terminal t, int row, int count)
    {
        var line = t.Buffer.Lines[row]!;
        return string.Concat(Enumerable.Range(0, count)
            .Select(i => string.IsNullOrEmpty(line[i].Content) ? " " : line[i].Content));
    }

    [Fact]
    public void Fill_covers_the_inclusive_rectangle_and_nothing_else()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[42;2;2;3;4$x");   // '*' from (2,2) to (3,4)

        Assert.Equal("          ", Row(terminal, 0, 10));
        Assert.Equal(" ***      ", Row(terminal, 1, 10));
        Assert.Equal(" ***      ", Row(terminal, 2, 10));
        Assert.Equal("          ", Row(terminal, 3, 10));
    }

    [Fact]
    public void A_rectangle_is_addressed_in_the_origin_modes_coordinates()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[2;5r{Esc}[?6h");
        terminal.Write($"{Esc}[42;1;1;1;2$x");   // region-relative (1,1)..(1,2)

        Assert.Equal("**        ", Row(terminal, 1, 10));   // absolute row 2
    }

    [Fact]
    public void A_rectangle_ignores_the_margins_it_crosses()
    {
        // "Ignores margins" is the standard's phrase: the fill is clipped to the SCREEN only.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?69h{Esc}[3;5s");
        terminal.Write($"{Esc}[42;1;1;1;8$x");

        Assert.Equal("********  ", Row(terminal, 0, 10));
    }

    [Fact]
    public void An_inverted_rectangle_refuses_the_whole_operation()
    {
        var terminal = NewTerminal();
        terminal.Write("abc");
        terminal.Write($"{Esc}[42;3;3;2;2$x");

        Assert.Equal("abc", Row(terminal, 0, 3));
    }

    [Fact]
    public void Fill_does_not_move_the_cursor()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;4H{Esc}[42;1;1;2;2$x");

        Assert.Equal(2, terminal.Buffer.Y);
        Assert.Equal(3, terminal.Buffer.X);
    }

    [Fact]
    public void Erase_leaves_blanks_where_the_rectangle_was()
    {
        var terminal = NewTerminal();
        terminal.Write("abcdefgh");
        terminal.Write($"{Esc}[1;3;1;5$z");

        Assert.Equal("ab   fgh", Row(terminal, 0, 8));
    }

    [Fact]
    public void Copy_snapshots_the_source_so_overlap_cannot_smear()
    {
        var terminal = NewTerminal();
        terminal.Write("abcdef");
        // Copy (1,1)..(1,4) one column right: overlapping in the smearing direction.
        terminal.Write($"{Esc}[1;1;1;4;1;1;2;1$v");

        Assert.Equal("aabcdf", Row(terminal, 0, 6));
    }

    [Fact]
    public void Copy_carries_the_attributes_with_the_characters()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1mZ{Esc}[0m");
        terminal.Write($"{Esc}[1;1;1;1;1;2;1;1$v");

        Assert.True(terminal.Buffer.Lines[1]![0].Attributes.IsBold());
        Assert.Equal("Z", terminal.Buffer.Lines[1]![0].Content);
    }

    [Fact]
    public void Copy_clips_a_destination_hanging_off_the_screen()
    {
        var terminal = NewTerminal(cols: 6, rows: 3);
        terminal.Write("abcdef");
        terminal.Write($"{Esc}[1;1;1;6;1;1;5;1$v");   // dest starts at column 5: only two fit

        Assert.Equal("abcdab", Row(terminal, 0, 6));
    }

    // ---- DECSERA (CSI Pt;Pl;Pb;Pr $ {) -------------------------------------------------------

    [Fact]
    public void SelectiveErase_blanks_the_rectangle()
    {
        var terminal = NewTerminal(cols: 6, rows: 3);
        terminal.Write("abcdef\r\nghijkl");
        terminal.Write($"{Esc}[1;2;2;5${{");

        Assert.Equal("a    f", Row(terminal, 0, 6));
        Assert.Equal("g    l", Row(terminal, 1, 6));
    }

    [Fact]
    public void SelectiveErase_spares_DecscaProtected_cells_only()
    {
        var terminal = NewTerminal(cols: 6, rows: 2);
        terminal.Write("ab");
        terminal.Write($"{Esc}[1\"q" + "CD" + $"{Esc}[0\"q");   // CD protected by DECSCA
        terminal.Write($"{Esc}Ve{Esc}W");                        // e guarded by ISO SPA/EPA
        terminal.Write($"{Esc}[1;1;1;6${{");

        // DECSCA protection holds; the ISO guard belongs to the other erase family and does not.
        Assert.Equal("  CD  ", Row(terminal, 0, 6));
    }

    private static AttributeData AttrAt(Terminal t, int row, int col) =>
        t.Buffer.Lines[row]![col].Attributes;

    [Fact]
    public void ChangeAttributes_sets_the_named_attributes_and_leaves_the_text_alone()
    {
        var terminal = NewTerminal(cols: 10, rows: 4);
        terminal.Write("XXXXXXXXXX\r\nXXXXXXXXXX\r\nXXXXXXXXXX");
        terminal.Write($"{Esc}[2;2;2;4;1;4$r");   // DECCARA: bold + underline over row 2, cols 2-4

        Assert.Equal("XXXXXXXXXX", Row(terminal, 1, 10));   // characters untouched
        Assert.False(AttrAt(terminal, 1, 0).IsBold());
        Assert.True(AttrAt(terminal, 1, 1).IsBold());
        Assert.True(AttrAt(terminal, 1, 1).IsUnderline());
        Assert.True(AttrAt(terminal, 1, 3).IsBold());
        Assert.False(AttrAt(terminal, 1, 4).IsBold());
        Assert.False(AttrAt(terminal, 0, 1).IsBold());      // the row above is not in the area
    }

    /// <summary>
    /// DECSACE's default is the STREAM: the area runs from the top-left position to the bottom-right
    /// one across whole intervening lines, not as a box. This is the setting that was accepted,
    /// reported back by DECRQSS and then read by nothing, because the controls it governs did not
    /// exist.
    /// </summary>
    [Fact]
    public void ChangeAttributes_runs_as_a_stream_by_default()
    {
        var terminal = NewTerminal(cols: 10, rows: 4);
        terminal.Write($"{Esc}[2;3;3;6;1$r");   // rows 2-3, cols 3..6

        // Row 2 runs from its column to the end of the line.
        Assert.False(AttrAt(terminal, 1, 1).IsBold());
        Assert.True(AttrAt(terminal, 1, 2).IsBold());
        Assert.True(AttrAt(terminal, 1, 9).IsBold());

        // Row 3 runs from the start of the line to its column.
        Assert.True(AttrAt(terminal, 2, 0).IsBold());
        Assert.True(AttrAt(terminal, 2, 5).IsBold());
        Assert.False(AttrAt(terminal, 2, 6).IsBold());
    }

    [Fact]
    public void ChangeAttributes_confines_itself_to_the_rectangle_under_Decsace_2()
    {
        var terminal = NewTerminal(cols: 10, rows: 4);
        terminal.Write($"{Esc}[2*x");           // DECSACE 2 -- rectangle
        terminal.Write($"{Esc}[2;3;3;6;1$r");

        foreach (var row in new[] { 1, 2 })
        {
            Assert.False(AttrAt(terminal, row, 1).IsBold());
            Assert.True(AttrAt(terminal, row, 2).IsBold());
            Assert.True(AttrAt(terminal, row, 5).IsBold());
            Assert.False(AttrAt(terminal, row, 6).IsBold());
        }
    }

    [Fact]
    public void ChangeAttributes_0_clears_the_four_DEC_attributes_but_not_invisible()
    {
        var terminal = NewTerminal(cols: 6, rows: 2);
        terminal.Write($"{Esc}[1;4;5;7;8mXXXX");
        terminal.Write($"{Esc}[2*x{Esc}[1;1;1;4;0$r");

        var attributes = AttrAt(terminal, 0, 0);
        Assert.False(attributes.IsBold());
        Assert.False(attributes.IsUnderline());
        Assert.False(attributes.IsBlink());
        Assert.False(attributes.IsInverse());

        // xterm leaves invisible out of the SGR_MASK that parameter 0 covers; it has its own
        // 8 and 28, which is the extension the documentation calls out.
        Assert.True(attributes.IsInvisible());
    }

    [Fact]
    public void ChangeAttributes_ignores_everything_outside_the_DEC_set()
    {
        var terminal = NewTerminal(cols: 6, rows: 2);
        terminal.Write("XXXX");
        terminal.Write($"{Esc}[2*x{Esc}[1;1;1;4;31;3;1$r");   // red and italic are not DECCARA's

        Assert.True(AttrAt(terminal, 0, 0).IsBold());
        Assert.False(AttrAt(terminal, 0, 0).IsItalic());
        Assert.Equal(AttributeData.Default.Fg, AttrAt(terminal, 0, 0).Fg);
    }

    [Fact]
    public void ReverseAttributes_toggles_each_cell_from_what_it_already_had()
    {
        var terminal = NewTerminal(cols: 6, rows: 2);
        terminal.Write($"{Esc}[1mXX{Esc}[0mXX");
        terminal.Write($"{Esc}[2*x{Esc}[1;1;1;4;1$t");        // DECRARA: reverse bold

        Assert.False(AttrAt(terminal, 0, 0).IsBold());
        Assert.False(AttrAt(terminal, 0, 1).IsBold());
        Assert.True(AttrAt(terminal, 0, 2).IsBold());
        Assert.True(AttrAt(terminal, 0, 3).IsBold());
        Assert.Equal("XXXX  ", Row(terminal, 0, 6));
    }

    [Fact]
    public void ReverseAttributes_0_reverses_the_four_together()
    {
        var terminal = NewTerminal(cols: 6, rows: 2);
        terminal.Write($"{Esc}[1;5mXX");
        terminal.Write($"{Esc}[2*x{Esc}[1;1;1;2;0$t");

        var attributes = AttrAt(terminal, 0, 0);
        Assert.False(attributes.IsBold());        // was on
        Assert.False(attributes.IsBlink());       // was on
        Assert.True(attributes.IsUnderline());    // was off
        Assert.True(attributes.IsInverse());      // was off
    }

    /// <summary>
    /// The resets say nothing under DECRARA -- reversing an attribute already covers both
    /// directions -- so xterm reads 22, 24, 25, 27 and 28 only when setting.
    /// </summary>
    [Fact]
    public void ReverseAttributes_ignores_the_reset_parameters()
    {
        var terminal = NewTerminal(cols: 6, rows: 2);
        terminal.Write($"{Esc}[1mXX");
        terminal.Write($"{Esc}[2*x{Esc}[1;1;1;2;22$t");

        Assert.True(AttrAt(terminal, 0, 0).IsBold());
    }

    [Fact]
    public void ChangeAttributes_marks_both_halves_of_a_wide_character()
    {
        // The trailing half holds no character of its own, which is exactly the cell xterm's
        // never-drawn test would skip. Skipping it here would leave one character disagreeing
        // with itself about how it is drawn.
        var terminal = NewTerminal(cols: 6, rows: 2);
        terminal.Write("世");
        terminal.Write($"{Esc}[2*x{Esc}[1;1;1;2;1$r");

        Assert.True(AttrAt(terminal, 0, 0).IsBold());
        Assert.True(AttrAt(terminal, 0, 1).IsBold());
    }

    /// <summary>
    /// DECSACE survives a soft reset and not a hard one, which is how xterm clears it. It mattered
    /// only once DECCARA and DECRARA read it: a stale rectangle setting turns the next program's
    /// stream into a box.
    /// </summary>
    [Fact]
    public void Decsace_survives_a_soft_reset_and_not_a_hard_one()
    {
        var terminal = NewTerminal(cols: 10, rows: 4);

        terminal.Write($"{Esc}[2*x{Esc}[!p");             // DECSACE 2, then DECSTR
        terminal.Write($"{Esc}[2;3;3;6;1$r");
        Assert.False(AttrAt(terminal, 1, 9).IsBold());    // still a rectangle

        terminal.Write($"{Esc}c");                        // RIS
        terminal.Write($"{Esc}[2;3;3;6;1$r");
        Assert.True(AttrAt(terminal, 1, 9).IsBold());     // back to a stream
    }

    /// <summary>
    /// The whole family is VT400, which is the gate xterm puts on each of them and what this
    /// terminal's own primary DA already says by advertising attribute 28 only from level 64.
    /// A program that lowered the level with DECSCL asked to be treated as older hardware.
    /// </summary>
    [Theory]
    [InlineData("[42;1;1;2;4$x")]     // DECFRA
    [InlineData("[1;1;2;4$z")]        // DECERA
    [InlineData("[1;1;2;4${")]        // DECSERA
    [InlineData("[1;1;1;4;2;1$v")]    // DECCRA
    [InlineData("[1;1;2;4;1$r")]      // DECCARA
    [InlineData("[1;1;2;4;1$t")]      // DECRARA
    public void The_rectangle_family_is_refused_below_level_64(string sequence)
    {
        var terminal = NewTerminal();
        terminal.Write("abcdefghij");
        var before = Row(terminal, 0, 10);
        var boldBefore = AttrAt(terminal, 0, 0).IsBold();

        terminal.Write($"{Esc}[62\"p");        // DECSCL: VT200
        terminal.Write($"{Esc}{sequence}");

        Assert.Equal(before, Row(terminal, 0, 10));
        Assert.Equal(boldBefore, AttrAt(terminal, 0, 0).IsBold());
    }

    [Fact]
    public void The_rectangle_family_works_again_at_level_64()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[62\"p{Esc}[64\"p");   // down to VT200 and back up to VT400
        terminal.Write($"{Esc}[42;1;1;1;3$x");

        Assert.Equal("***       ", Row(terminal, 0, 10));
    }

    /// <summary>
    /// DECSACE is the family's one exception, and it is xterm's asymmetry rather than an oversight:
    /// its handler has no level test where every neighbour has one. Storing which extent a program
    /// would prefer changes nothing by itself -- the two controls that read it are gated -- so
    /// there is nothing to refuse.
    /// </summary>
    [Fact]
    public void Decsace_is_stored_at_every_level()
    {
        var terminal = NewTerminal();
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);

        terminal.Write($"{Esc}[62\"p");        // VT200
        terminal.Write($"{Esc}[2*x");          // DECSACE 2
        terminal.Write($"{Esc}P$q*x{Esc}\\");  // DECRQSS

        Assert.Equal($"{Esc}P1$r2*x{Esc}\\", Assert.Single(replies));
    }
}
