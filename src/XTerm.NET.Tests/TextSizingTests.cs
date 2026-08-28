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
    [InlineData("q=1")]          // not a key of this protocol
    [InlineData("s")]            // not a pair
    [InlineData("s=x")]
    [InlineData("s=-1")]
    public void Metadata_out_of_range_is_not_handled(string metadata)
    {
        Assert.False(TextSizing.TryParse(metadata, out _));

        var t = Fresh();
        var recognized = true;
        t.OscReceived += (_, e) => recognized = e.Recognized;
        t.Write(Sized(metadata, "X"));

        Assert.False(recognized);
        Assert.Equal(0, t.Buffer.X);
        Assert.False(Row(t).HasSizedRuns);
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
}
