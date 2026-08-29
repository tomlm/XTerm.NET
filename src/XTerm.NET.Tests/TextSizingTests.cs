using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// The Kitty text sizing protocol, OSC 66.
///
/// <para>Two halves, and they are worth keeping apart. The WIDTH half is the emulator's own: a run
/// really claims <c>s * w</c> columns, so the cursor, selection and search agree with the client
/// about how much room it took — which is the point of the <c>w</c> key, a client stating a
/// string's width instead of both sides guessing at Unicode. The SCALE half the emulator only
/// records, on the line, for a renderer to draw.</para>
/// </summary>
public class TextSizingTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    private static string Sized(string metadata, string text) => $"{Esc}]66;{metadata};{text}{St}";

    private static Terminal Fresh(int cols = 20, int rows = 5)
        => new(new TerminalOptions { Cols = cols, Rows = rows });

    private static BufferLine Row(Terminal t, int row = 0) => t.Buffer.Lines[t.Buffer.YBase + row]!;

    [Fact]
    public void Scale_makes_each_character_claim_that_many_columns()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "abc"));

        // Three blocks of two columns: text in the first cell of each, a continuation after it.
        Assert.Equal("a", Row(t)[0].Content);
        Assert.Equal(2, Row(t)[0].Width);
        Assert.Equal(0, Row(t)[1].Width);
        Assert.Equal("b", Row(t)[2].Content);
        Assert.Equal("c", Row(t)[4].Content);
        Assert.Equal(6, t.Buffer.X);
    }

    [Fact]
    public void Width_puts_the_whole_run_in_the_cells_it_asked_for()
    {
        var t = Fresh();
        t.Write(Sized("n=1:d=2:w=1", "ab"));

        Assert.Equal("ab", Row(t)[0].Content);
        Assert.Equal(1, Row(t)[0].Width);
        Assert.Equal(1, t.Buffer.X);
    }

    [Fact]
    public void Scale_and_width_together_give_a_block_of_scale_times_width()
    {
        var t = Fresh();
        t.Write(Sized("s=2:w=3", "hi"));

        Assert.Equal("hi", Row(t)[0].Content);
        Assert.Equal(6, Row(t)[0].Width);
        Assert.Equal(6, t.Buffer.X);
    }

    /// <summary>What the protocol's own capability probe measures.</summary>
    [Fact]
    public void The_cursor_advance_reports_support_the_way_the_probe_expects()
    {
        var t = Fresh();

        t.Write(Sized("w=2", " "));
        Assert.Equal(2, t.Buffer.X);

        t.Write(Sized("s=2", " "));
        Assert.Equal(4, t.Buffer.X);
    }

    [Fact]
    public void The_run_is_recorded_on_the_line_with_what_was_asked_for()
    {
        var t = Fresh();
        t.Write(Sized("s=3:n=1:d=2:v=1:h=2", "x"));

        Assert.True(Row(t).TryGetSizedRunAt(1, out var run));
        Assert.Equal(0, run.Column);
        Assert.Equal(3, run.Cols);
        Assert.Equal(3, run.Rows);
        Assert.Equal(3, run.Sizing.Scale);
        Assert.True(run.Sizing.IsFractional);
        Assert.Equal(TextSizeVerticalAlignment.Bottom, run.Sizing.VerticalAlignment);
        Assert.Equal(TextSizeHorizontalAlignment.Center, run.Sizing.HorizontalAlignment);
    }

    [Fact]
    public void Adjacent_runs_with_the_same_sizing_are_one_span()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "ab"));

        Assert.Single(Row(t).SizedRuns);
        Assert.Equal(4, Row(t).SizedRuns[0].Cols);
    }

    [Fact]
    public void Ordinary_text_is_not_a_sized_run()
    {
        var t = Fresh();
        t.Write("plain");

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(1, Row(t)[0].Width);
    }

    [Fact]
    public void A_block_that_does_not_fit_wraps_whole()
    {
        var t = Fresh(cols: 5);
        t.Write("ab" + Sized("s=2:w=2", "X"));

        // Four columns will not fit after "ab", so the block goes to the next line rather than
        // being split across the edge.
        Assert.Equal("b", Row(t)[1].Content);
        Assert.Equal("X", Row(t, 1)[0].Content);
        Assert.Equal(4, Row(t, 1)[0].Width);
        Assert.Equal(4, t.Buffer.X);
        Assert.Equal(1, t.Buffer.Y);
    }

    [Fact]
    public void With_wrapping_off_the_block_is_moved_back_to_fit()
    {
        var t = Fresh(cols: 5);
        t.Options.Wraparound = false;
        t.Write("ab" + Sized("s=2:w=2", "X"));

        Assert.Equal(0, t.Buffer.Y);
        Assert.Equal("X", Row(t)[1].Content);
        Assert.Equal(4, Row(t)[1].Width);
        Assert.Equal(5, t.Buffer.X);
    }

    [Fact]
    public void A_block_wider_than_the_screen_is_discarded()
    {
        var t = Fresh(cols: 5);
        t.Write(Sized("s=2:w=7", "X"));

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(0, t.Buffer.X);
    }

    [Fact]
    public void Writing_over_part_of_a_block_erases_all_of_it()
    {
        var t = Fresh();
        t.Write(Sized("s=2:w=2", "X"));       // columns 0..3
        t.Write($"{Esc}[1;3H" + "y");         // over column 2

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal("y", Row(t)[2].Content);

        // The rest of the block is gone rather than left claiming columns it no longer owns.
        Assert.Equal(" ", Row(t)[0].Content);
        Assert.Equal(1, Row(t)[0].Width);
        Assert.Equal(" ", Row(t)[3].Content);
    }

    [Fact]
    public void Writing_a_new_block_over_an_old_one_replaces_it()
    {
        var t = Fresh();
        t.Write(Sized("s=2:w=2", "X"));
        t.Write($"{Esc}[1;1H" + Sized("s=2", "y"));

        Assert.Single(Row(t).SizedRuns);
        Assert.Equal(2, Row(t).SizedRuns[0].Cols);
        Assert.Equal("y", Row(t)[0].Content);
        Assert.Equal(" ", Row(t)[2].Content);
        Assert.Equal(" ", Row(t)[3].Content);
    }

    [Fact]
    public void An_empty_payload_draws_nothing_and_moves_nothing()
    {
        var t = Fresh();
        t.Write(Sized("s=2", ""));

        Assert.Equal(0, t.Buffer.X);
        Assert.False(Row(t).HasSizedRuns);
    }

    [Fact]
    public void Semicolons_in_the_text_are_text()
    {
        var t = Fresh();
        t.Write(Sized("w=3", "a;b"));

        Assert.Equal("a;b", Row(t)[0].Content);
        Assert.Equal(3, Row(t)[0].Width);
    }

    [Fact]
    public void A_wide_character_keeps_its_own_width_inside_the_scale()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "\u4F60"));   // a CJK ideograph, two columns before scaling

        Assert.Equal(4, Row(t)[0].Width);
        Assert.Equal(4, t.Buffer.X);
    }

    [Fact]
    public void Erasing_the_line_erases_the_run()
    {
        var t = Fresh();
        t.Write(Sized("s=2:w=2", "X"));
        t.Write($"{Esc}[1;3H" + $"{Esc}[K");   // erase from column 3 rightwards

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(" ", Row(t)[0].Content);
        Assert.Equal(1, Row(t)[0].Width);
    }

    [Fact]
    public void Erasing_characters_erases_a_block_they_touch()
    {
        var t = Fresh();
        t.Write(Sized("s=2:w=2", "X"));
        t.Write($"{Esc}[1;4H" + $"{Esc}[1X");   // ECH over the block's last column

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(1, Row(t)[0].Width);
    }

    [Fact]
    public void Shifting_cells_erases_a_block_they_belong_to()
    {
        var t = Fresh();
        t.Write(Sized("s=2:w=2", "X"));

        // ICH at the block's second column: the cells move, so the block is gone rather than left
        // described by columns that now hold something else.
        t.Write($"{Esc}[1;2H" + $"{Esc}[2@");

        Assert.False(Row(t).HasSizedRuns);
        for (var col = 0; col < 6; col++)
            Assert.True(Row(t)[col].Width <= 1, $"column {col} still claims columns");
    }

    [Fact]
    public void Deleting_characters_erases_a_block_they_belong_to()
    {
        var t = Fresh();
        t.Write("ab" + Sized("s=2:w=2", "X"));
        t.Write($"{Esc}[1;1H" + $"{Esc}[1P");

        Assert.False(Row(t).HasSizedRuns);
        for (var col = 0; col < 6; col++)
            Assert.True(Row(t)[col].Width <= 1, $"column {col} still claims columns");
    }

    /// <summary>
    /// The protocol's rule for text aimed at a row a taller block already occupies: the cursor moves
    /// past the block's cells and the text lands after them. Without it a client printing normally
    /// under a heading has its output drawn over by the heading's lower half.
    /// </summary>
    [Fact]
    public void Text_under_a_tall_block_is_pushed_past_it()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "Big"));    // three 2-column blocks, two rows tall, columns 0..5
        t.Write("\r\nxyz");

        Assert.Equal("x", Row(t, 1)[6].Content);
        Assert.Equal("y", Row(t, 1)[7].Content);
        Assert.Equal("z", Row(t, 1)[8].Content);
        Assert.Equal(" ", Row(t, 1)[0].Content);
        Assert.Equal(9, t.Buffer.X);
    }

    [Fact]
    public void The_row_below_a_one_row_block_is_ordinary()
    {
        var t = Fresh();
        t.Write(Sized("w=2", "X"));      // two columns, but only one row tall
        t.Write("\r\nxyz");

        Assert.Equal("x", Row(t, 1)[0].Content);
    }

    [Fact]
    public void The_rows_a_block_occupies_are_answerable()
    {
        var t = Fresh();
        t.Write(Sized("s=3:w=2", "X"));  // 6 columns, 3 rows

        var top = t.Buffer.YBase;
        Assert.True(t.Buffer.TryGetSizedRunCovering(top + 1, 5, out var run, out var anchor));
        Assert.Equal(top, anchor);
        Assert.Equal(3, run.Rows);
        Assert.True(t.Buffer.TryGetSizedRunCovering(top + 2, 0, out _, out _));

        // Its own row is not "covered from above", and the row past its height is not covered at all.
        Assert.False(t.Buffer.TryGetSizedRunCovering(top, 0, out _, out _));
        Assert.False(t.Buffer.TryGetSizedRunCovering(top + 3, 0, out _, out _));
        Assert.False(t.Buffer.TryGetSizedRunCovering(top + 1, 6, out _, out _));
    }

    /// <summary>
    /// The payload of an OSC is not a preceding graphic character, so <c>CSI b</c> has nothing to
    /// repeat — replaying a scaled block as plain cells is neither what was printed nor what was
    /// asked for.
    /// </summary>
    [Fact]
    public void A_sized_block_is_not_repeated_by_rep()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "X"));
        t.Write($"{Esc}[3b");

        Assert.Equal(2, t.Buffer.X);
        Assert.Equal(" ", Row(t)[2].Content);
        Assert.Single(Row(t).SizedRuns);
    }

    [Fact]
    public void Insert_mode_shifts_the_rest_of_the_line_intact()
    {
        var t = Fresh();
        t.Write(Sized("s=2:w=2", "X"));   // columns 0..3
        t.Write("tail");                  // columns 4..7

        t.Write($"{Esc}[1;5H{Esc}[4h");   // cursor to column 5, IRM on
        t.Write(Sized("w=2", "Z"));

        Assert.Equal("XZtail", Row(t).TranslateToString(trimRight: true));
        Assert.Equal(2, Row(t)[4].Width);
        Assert.Equal("t", Row(t)[6].Content);

        // The block that was not shifted is untouched.
        Assert.True(Row(t).TryGetSizedRunAt(0, out var first));
        Assert.Equal(4, first.Cols);
    }

    [Fact]
    public void Insert_mode_over_a_block_erases_it()
    {
        var t = Fresh();
        t.Write(Sized("s=2:w=2", "X"));
        t.Write($"{Esc}[1;2H{Esc}[4h" + "a");

        Assert.False(Row(t).HasSizedRuns);
        for (var col = 0; col < 8; col++)
            Assert.True(Row(t)[col].Width <= 1, $"column {col} still claims columns");
    }

    /// <summary>
    /// Widening moves no cell of a group holding a block — reflow leaves such a group alone — so the
    /// block is still where its run says it is, and the run is still worth keeping.
    /// </summary>
    [Fact]
    public void A_block_survives_the_line_growing()
    {
        var t = Fresh(cols: 10, rows: 4);
        t.Write(Sized("s=2:w=2", "Z"));

        t.Resize(14, 4);

        Assert.True(Row(t).TryGetSizedRunAt(0, out var run));
        Assert.Equal(4, run.Cols);
        Assert.Equal("Z", Row(t).TranslateToString(trimRight: true));
    }

    /// <summary>
    /// A block cut in half by a narrowing does not survive it: the columns holding the rest of the
    /// glyph are gone, so what is left becomes spaces rather than a cell claiming columns that no
    /// longer exist.
    /// </summary>
    [Fact]
    public void A_block_cut_by_a_narrowing_is_dropped()
    {
        var t = Fresh(cols: 10, rows: 4);
        t.Write($"{Esc}[1;7H" + Sized("s=2:w=2", "Z"));   // columns 6..9

        t.Resize(8, 4);

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(" ", Row(t)[6].Content);
        Assert.Equal(1, Row(t)[6].Width);
    }

    /// <summary>
    /// Reflow redistributes cells between lines and run metadata cannot travel with them, so a
    /// wrapped group holding a block is left alone — as a double-width line already is. Without
    /// that, the compaction copies read cells the same pass had already blanked.
    /// </summary>
    [Fact]
    public void Reflow_does_not_garble_a_wrapped_group_holding_a_block()
    {
        var t = Fresh(cols: 10, rows: 4);
        t.Write("0123456789");            // fills the row, so the next write wraps
        t.Write("ab" + Sized("s=2:w=2", "Z") + "cd");

        t.Resize(14, 4);
        Assert.Contains("Z", Row(t, 0).TranslateToString() + Row(t, 1).TranslateToString());
        Assert.Contains("cd", Row(t, 0).TranslateToString() + Row(t, 1).TranslateToString());

        // Narrowing does not re-wrap that group either, so it loses what no longer fits -- the same
        // cost a double-width line already pays here. What it must not do is garble what remains.
        t.Resize(6, 4);
        var all = string.Concat(Enumerable.Range(0, t.Buffer.Lines.Length)
            .Select(i => t.Buffer.Lines[i]?.TranslateToString() ?? string.Empty));
        Assert.Contains("Z", all);
        Assert.Contains("012345", all);

        for (var i = 0; i < t.Buffer.Lines.Length; i++)
        {
            var line = t.Buffer.Lines[i];
            if (line is null)
                continue;

            for (var col = 0; col < line.Length; col++)
                Assert.True(col + line[col].Width <= line.Length, $"line {i} column {col} runs off the end");
        }
    }

    [Fact]
    public void A_recycled_line_keeps_no_runs()
    {
        // No scrollback, so the line holding the run is the very object the ring hands back for
        // the new bottom row -- which is what makes clearing it on reuse necessary.
        var t = new Terminal(new TerminalOptions { Cols = 10, Rows = 2, Scrollback = 0 });
        t.Write(Sized("s=2", "X"));
        t.Write("\r\n\r\n\r\n");

        for (var i = 0; i < t.Buffer.Lines.Length; i++)
        {
            var line = t.Buffer.Lines[i];
            if (line is not null)
                Assert.False(line.HasSizedRuns);
        }
    }

    [Theory]
    [InlineData("s=0")]          // scale starts at one
    [InlineData("s=8")]          // and stops at seven
    [InlineData("w=8")]
    [InlineData("n=16")]
    [InlineData("d=16")]
    [InlineData("n=3:d=2")]      // a denominator must exceed its numerator
    [InlineData("v=3")]
    [InlineData("h=3")]
    [InlineData("s")]            // not a pair
    [InlineData("s=x")]
    [InlineData("s=-1")]
    [InlineData("s=+2")]         // the grammar is digits, not int.TryParse's idea of a number
    [InlineData("s= 2")]
    public void Metadata_out_of_range_is_not_handled(string metadata)
    {
        Assert.False(TextSizing.TryParse(metadata, out _));

        var t = Fresh();
        var recognized = true;
        t.OscReceived += (_, e) => recognized = e.Recognized;
        t.Write(Sized(metadata, "X"));

        Assert.False(recognized);
        Assert.False(Row(t).HasSizedRuns);
    }

    /// <summary>
    /// The text is what the user was meant to read, so a metadata the terminal cannot honour costs
    /// the sizing rather than the heading.
    /// </summary>
    [Fact]
    public void Text_of_an_unhandled_sequence_is_still_printed()
    {
        var t = Fresh();
        t.Write(Sized("s=99", "Hi"));

        Assert.Equal("Hi", Row(t).TranslateToString(trimRight: true));
        Assert.Equal(2, t.Buffer.X);
        Assert.False(Row(t).HasSizedRuns);
    }

    /// <summary>
    /// This protocol has been extended before. A key from a later revision costs its own effect, not
    /// the run of text it was attached to.
    /// </summary>
    [Fact]
    public void An_unknown_key_is_ignored_and_the_rest_honoured()
    {
        Assert.True(TextSizing.TryParse("s=2:q=1", out var sizing));
        Assert.Equal(2, sizing.Scale);

        var t = Fresh();
        var recognized = false;
        t.OscReceived += (_, e) => recognized = e.Recognized;
        t.Write(Sized("s=2:q=1", "X"));

        Assert.True(recognized);
        Assert.Equal("X", Row(t)[0].Content);
        Assert.Equal(2, Row(t)[0].Width);
    }

    /// <summary>
    /// The protocol's 4096-byte payload limit belongs to the sequence, not to one of its modes.
    /// With w=0 every grapheme is interned in the process-wide cluster table, so an unbounded
    /// payload here is the more expensive of the two to let through.
    /// </summary>
    [Fact]
    public void An_oversized_payload_is_cut_to_the_protocol_limit_without_a_width_too()
    {
        var t = Fresh(cols: 80, rows: 4);
        t.Write(Sized("s=1", new string('x', 5000)));

        var printed = 0;
        for (var row = 0; row < t.Buffer.Lines.Length; row++)
        {
            var line = t.Buffer.Lines[row];
            if (line is null)
                continue;

            for (var col = 0; col < line.Length; col++)
            {
                if (line[col].Content == "x")
                    printed++;
            }
        }

        Assert.Equal(4096, printed);
    }

    [Fact]
    public void A_key_longer_than_one_letter_is_ignored_too()
    {
        Assert.True(TextSizing.TryParse("scale=2:s=3", out var sizing));
        Assert.Equal(3, sizing.Scale);
    }

    [Fact]
    public void Metadata_defaults_to_plain_text()
    {
        Assert.True(TextSizing.TryParse("", out var sizing));
        Assert.Equal(TextSizing.Default, sizing);
        Assert.True(sizing.IsDefault);
        Assert.False(sizing.IsFractional);
        Assert.Equal(1, sizing.Scale);
        Assert.Equal(0, sizing.Width);
    }

    [Fact]
    public void A_fraction_of_one_is_no_fraction()
    {
        Assert.False(TextSizing.TryParse("n=2:d=2", out _));
        Assert.True(TextSizing.TryParse("n=0:d=2", out var sizing));
        Assert.False(sizing.IsFractional);
    }

    [Fact]
    public void Unscaled_text_with_only_a_width_is_still_a_run()
    {
        // The width half of the protocol on its own: no scaling asked for, but the client is
        // telling the terminal how many cells its text takes.
        Assert.True(TextSizing.TryParse("w=2", out var sizing));
        Assert.False(sizing.IsDefault);
        Assert.Equal(1, sizing.Scale);
    }

    [Fact]
    public void A_link_covers_a_sized_run_written_inside_it()
    {
        var t = Fresh();
        t.Write($"{Esc}]8;;https://example.com{St}" + Sized("s=2", "X") + $"{Esc}]8;;{St}");

        Assert.True(Row(t).TryGetLinkAt(1, out var link));
        Assert.Equal("https://example.com", link.Url);
        Assert.Equal(2, link.Cols);
    }
    /// <summary>
    /// The protocol erases over a REGION, so clearing the screen below a block's own row still takes
    /// the block: two of its three rows were inside what was cleared.
    /// </summary>
    [Fact]
    public void Erasing_below_takes_a_block_hanging_into_it()
    {
        var t = Fresh();
        t.Write(Sized("s=3", "H"));
        t.Write($"{Esc}[2;1H{Esc}[J");   // cursor to row 2, erase below

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(" ", Row(t)[0].Content);
        Assert.False(t.Buffer.TryGetSizedRunCovering(t.Buffer.YBase + 1, 0, out _, out _));
    }

    /// <summary>
    /// Same rule, a line at a time and a few cells at a time.
    /// </summary>
    [Theory]
    [InlineData("[K")]      // erase to the right of a covered cell
    [InlineData("[2K")]     // erase the whole covered row
    [InlineData("[4X")]     // erase characters on the covered row
    public void Erasing_a_covered_row_takes_the_block(string erase)
    {
        var t = Fresh();
        t.Write(Sized("s=2", "H"));
        t.Write($"{Esc}[2;1H{Esc}{erase}");

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(" ", Row(t)[0].Content);
    }

    /// <summary>
    /// An erase that misses the block entirely leaves it alone -- the region rule is about
    /// intersection, not about the presence of a block anywhere above.
    /// </summary>
    [Fact]
    public void Erasing_beside_a_block_leaves_it_alone()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "H"));         // columns 0..1
        t.Write($"{Esc}[2;5H{Esc}[K");      // erase from column 5 rightwards on the row below

        Assert.True(Row(t).TryGetSizedRunAt(0, out _));
    }

    /// <summary>
    /// Splicing a line into the middle of a block would leave its lower rows stranded a row further
    /// down than the run says, so the block is erased instead.
    /// </summary>
    [Fact]
    public void Inserting_a_line_through_a_block_erases_it()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "Hi"));
        t.Write($"{Esc}[2;1H{Esc}[L");

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(" ", Row(t)[0].Content);
    }

    /// <summary>
    /// And deleting one of the rows a block hangs over does the same.
    /// </summary>
    [Fact]
    public void Deleting_a_covered_line_erases_the_block()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "Hi"));
        t.Write($"{Esc}[2;1H{Esc}[M");

        Assert.False(Row(t).HasSizedRuns);
    }

    /// <summary>
    /// The rule is about the cells the text will OVERWRITE, so a double-width character whose right
    /// half would land inside a block is moved past it too, not just one that starts there.
    /// </summary>
    [Fact]
    public void A_wide_character_may_not_overlap_a_block_from_the_left()
    {
        var t = Fresh();
        t.Write("x" + Sized("s=2", "A"));   // block at columns 1..2, two rows tall
        t.Write("\r\n\u4e2d");             // a CJK ideograph, two columns wide

        Assert.Equal(" ", Row(t, 1)[0].Content);
        Assert.Equal("\u4e2d", Row(t, 1)[3].Content);
    }

    /// <summary>
    /// Clearing the screen takes the last block with it, so the print path stops looking for rows
    /// hanging over -- a heading early in a session must not retire the fast path for the rest of it.
    /// </summary>
    [Fact]
    public void Clearing_the_screen_stops_the_search_for_blocks()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "H"));
        Assert.True(t.Buffer.HasMultiRowSizedRuns);

        t.Write($"{Esc}[2J");

        Assert.False(t.Buffer.HasMultiRowSizedRuns);
    }

    /// <summary>
    /// A row full of blocks that cannot be merged into one run is still skipped completely -- the
    /// loop's bound is for hostile input, not for a legal screen.
    /// </summary>
    [Fact]
    public void A_row_of_many_blocks_is_skipped_completely()
    {
        var t = Fresh(cols: 80, rows: 4);
        for (var i = 0; i < 20; i++)
            t.Write(Sized("s=2", "a") + Sized("s=2:n=1:d=2", "b"));   // 40 unmergeable blocks, 80 columns

        t.Write("\r\nZ");

        // Nowhere on the covered row is free, so the text lands on the row after it.
        Assert.Equal("Z", Row(t, 2)[0].Content);
        Assert.Equal(" ", Row(t, 1)[0].Content);
    }
    /// <summary>
    /// A scroll of a PARTIAL region splices a line out of the middle of the buffer exactly as
    /// <c>DL</c> does, so a block straddling the region's top boundary dies the same way.
    /// </summary>
    [Theory]
    [InlineData("[S")]      // scroll the region up
    [InlineData("[T")]      // and down
    public void A_region_scroll_erases_a_block_it_would_tear(string scroll)
    {
        var t = Fresh(rows: 6);
        t.Write($"{Esc}[1;1H" + Sized("s=2", "H"));   // anchored on row 0, reaching row 1
        t.Write($"{Esc}[2;5r");                        // region rows 2..5, so the block straddles its top
        t.Write($"{Esc}{scroll}");

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(" ", Row(t)[0].Content);
    }

    /// <summary>
    /// And one that reaches out of the region's BOTTOM: its lower rows stay where they are while
    /// the row describing them moves.
    /// </summary>
    [Fact]
    public void A_region_scroll_erases_a_block_reaching_below_it()
    {
        var t = Fresh(rows: 6);
        t.Write($"{Esc}[2;5r{Esc}[5;1H" + Sized("s=2", "H"));   // anchored on the region's last row
        t.Write($"{Esc}[S");

        Assert.False(t.Buffer.HasMultiRowSizedRuns);
    }

    /// <summary>
    /// A block wholly inside the region travels with its rows, which move together -- a scroll is
    /// not an erase.
    /// </summary>
    [Fact]
    public void A_region_scroll_carries_a_block_that_fits_inside_it()
    {
        var t = Fresh(rows: 6);
        t.Write($"{Esc}[2;5r{Esc}[3;1H" + Sized("s=2", "H"));   // rows 2..3, inside the region
        t.Write($"{Esc}[S");

        Assert.True(Row(t, 1).TryGetSizedRunAt(0, out var run));
        Assert.Equal(2, run.Rows);
        Assert.Equal("H", Row(t, 1)[0].Content);
    }
}
