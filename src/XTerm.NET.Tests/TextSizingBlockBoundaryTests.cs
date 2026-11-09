using XTerm.Buffer;
using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// OSC 66 blocks that share a sizing are joined into one span on the line, but each is still its own
/// multicell character: overwriting part of one erases that block and no other.
/// </summary>
/// <remarks>
/// tomlm/Iciclecreek.Avalonia.Terminal#211 -- OpenCode prints each CJK character as its own
/// <c>w=2</c> block, and a Backspace that overwrote the last one with two spaces blanked the line.
/// </remarks>
public class TextSizingBlockBoundaryTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    private static string Sized(string metadata, string text) => $"{Esc}]66;{metadata};{text}{St}";

    private static string Blocks(string metadata, string chars)
        => string.Concat(chars.Select(c => Sized(metadata, c.ToString())));

    private static Terminal Fresh(int cols = 40, int rows = 4)
        => new(new TerminalOptions { Cols = cols, Rows = rows });

    private static BufferLine Row(Terminal t, int row = 0) => t.Buffer.Lines[t.Buffer.YBase + row]!;

    private static string Text(Terminal t, int row = 0) => t.GetVisibleLines()[row].TrimEnd();

    private static (int Column, int End)[] Spans(Terminal t, int row = 0)
        => Row(t, row).SizedRuns.Select(r => (r.Column, r.EndColumn)).ToArray();

    [Fact]
    public void Overwriting_the_last_of_three_wide_blocks_keeps_the_other_two()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));
        Assert.Equal("甲乙丙", Text(t));

        t.Write($"{Esc}[1;5H  ");

        Assert.Equal("甲乙", Text(t));
        Assert.Equal(new[] { (0, 4) }, Spans(t));
    }

    [Fact]
    public void Overwriting_the_last_of_three_narrow_blocks_keeps_the_other_two()
    {
        var t = Fresh();
        t.Write(Blocks("w=1", "ABC"));

        t.Write($"{Esc}[1;3H ");

        Assert.Equal("AB", Text(t));
    }

    [Fact]
    public void Overwriting_the_first_block_keeps_the_rest()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));

        t.Write($"{Esc}[1;1H  ");

        Assert.Equal("  乙丙", Text(t));
        Assert.Equal(new[] { (2, 6) }, Spans(t));
    }

    [Fact]
    public void Overwriting_the_middle_block_splits_the_span_around_it()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));

        t.Write($"{Esc}[1;3H  ");

        Assert.Equal("甲  丙", Text(t));

        // Left to right, as SizedRuns is documented, and each still answers for its own columns.
        Assert.Equal(new[] { (0, 2), (4, 6) }, Spans(t));
        Assert.False(Row(t).TryGetSizedRunAt(2, out _));
        Assert.True(Row(t).TryGetSizedRunAt(5, out var run));
        Assert.Equal(4, run.Column);
    }

    [Fact]
    public void A_replacement_character_over_half_a_block_erases_only_that_block()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));

        // Column 4 is the first half of the third block.
        t.Write($"{Esc}[1;5HX");

        Assert.Equal("甲乙X", Text(t));
        Assert.Equal(" ", Row(t)[5].Content);
        Assert.Equal(new[] { (0, 4) }, Spans(t));
    }

    [Fact]
    public void Writing_over_the_second_half_of_a_block_erases_its_first_half_too()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));

        // Column 3 is the continuation of the middle block: the block it belongs to starts left of
        // the write, and that whole block goes -- but only that one.
        t.Write($"{Esc}[1;4HZ");

        Assert.Equal("甲 Z丙", Text(t));
        Assert.Equal(" ", Row(t)[2].Content);
        Assert.Equal(1, Row(t)[2].Width);
        Assert.Equal(new[] { (0, 2), (4, 6) }, Spans(t));
    }

    [Fact]
    public void A_wide_character_straddling_two_blocks_erases_both_and_no_more()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙丁"));

        // Columns 3..4: the second half of 乙 and the first half of 丙.
        t.Write($"{Esc}[1;4H中");

        Assert.Equal("甲 中 丁", Text(t));
        Assert.Equal(new[] { (0, 2), (6, 8) }, Spans(t));
    }

    [Fact]
    public void Erase_in_line_keeps_the_blocks_before_the_cursor()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));

        t.Write($"{Esc}[1;3H{Esc}[K");

        Assert.Equal("甲", Text(t));
        Assert.Equal(new[] { (0, 2) }, Spans(t));
    }

    [Fact]
    public void Erase_characters_keeps_the_blocks_either_side()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));

        t.Write($"{Esc}[1;3H{Esc}[2X");

        Assert.Equal("甲  丙", Text(t));
        Assert.Equal(new[] { (0, 2), (4, 6) }, Spans(t));
    }

    [Fact]
    public void Delete_characters_keeps_the_blocks_before_the_cursor()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));

        // A shift cannot carry a block along, so everything from the cursor on is blanked -- but
        // the block before the cursor was never in the way.
        t.Write($"{Esc}[1;3H{Esc}[2P");

        Assert.Equal("甲", Text(t));
        Assert.Equal(new[] { (0, 2) }, Spans(t));
    }

    [Fact]
    public void A_single_block_with_a_multi_character_payload_is_still_erased_whole()
    {
        var t = Fresh();
        t.Write(Sized("w=4", "甲乙"));

        t.Write($"{Esc}[1;4Hx");

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal("   x", Text(t));
    }

    [Fact]
    public void A_single_wide_block_is_erased_whole_by_writing_its_second_half()
    {
        var t = Fresh();
        t.Write(Sized("w=2", "甲"));

        t.Write($"{Esc}[1;2Hx");

        Assert.False(Row(t).HasSizedRuns);
        Assert.Equal(" x", Text(t));
        Assert.Equal(1, Row(t)[0].Width);
    }

    [Fact]
    public void A_multi_character_block_beside_others_is_erased_whole_and_alone()
    {
        var t = Fresh();
        // The same sizing all three times, so they share one span -- the middle block just carries
        // two characters in its two columns.
        t.Write(Sized("w=2", "甲") + Sized("w=2", "乙丙") + Sized("w=2", "丁"));
        Assert.Equal(new[] { (0, 6) }, Spans(t));

        t.Write($"{Esc}[1;4Hx");

        Assert.Equal("甲 x丁", Text(t));
        Assert.Equal(new[] { (0, 2), (4, 6) }, Spans(t));
    }

    [Fact]
    public void Scaled_blocks_from_one_sequence_are_separate_blocks()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "abc"));
        Assert.Equal(new[] { (0, 6) }, Spans(t));

        t.Write($"{Esc}[1;3Hx");

        Assert.Equal("a", Row(t)[0].Content);
        Assert.Equal("x", Row(t)[2].Content);
        Assert.Equal(" ", Row(t)[3].Content);
        Assert.Equal("c", Row(t)[4].Content);
        Assert.Equal(new[] { (0, 2), (4, 6) }, Spans(t));
    }

    [Fact]
    public void Writing_the_second_half_of_a_scaled_block_erases_all_of_it_and_only_it()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "abc"));

        // Column 3 is the second half of b. The wide-cell repair has blanked b's first cell to one
        // column before the run is told, so the block's start has to come from a's end.
        t.Write($"{Esc}[1;4HX");

        Assert.Equal("a", Row(t)[0].Content);
        Assert.Equal(" ", Row(t)[2].Content);
        Assert.Equal("X", Row(t)[3].Content);
        Assert.Equal("c", Row(t)[4].Content);
        Assert.Equal(new[] { (0, 2), (4, 6) }, Spans(t));

        // Nothing hangs over column 2 of the row below any more, so text lands there.
        t.Write($"{Esc}[2;3HY");
        Assert.Equal("Y", Row(t, 1)[2].Content);
    }

    [Fact]
    public void Scaled_blocks_of_different_widths_are_told_apart_by_their_cells()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "a甲b"));         // blocks of 2, 4 and 2 columns
        Assert.Equal(new[] { (0, 8) }, Spans(t));

        // Column 4 is the third column of the four-column block.
        t.Write($"{Esc}[1;5Hx");

        Assert.Equal("a", Row(t)[0].Content);
        Assert.Equal(" ", Row(t)[2].Content);
        Assert.Equal(1, Row(t)[2].Width);
        Assert.Equal("x", Row(t)[4].Content);
        Assert.Equal(" ", Row(t)[5].Content);
        Assert.Equal("b", Row(t)[6].Content);
        Assert.Equal(new[] { (0, 2), (6, 8) }, Spans(t));
    }

    [Fact]
    public void Erasing_the_row_under_a_tall_block_takes_only_the_block_above_it()
    {
        var t = Fresh();
        t.Write(Sized("s=2", "abc"));

        // Row 2, column 3 is under the second block's lower half.
        t.Write($"{Esc}[2;3H{Esc}[X");

        Assert.Equal("a", Row(t)[0].Content);
        Assert.Equal(" ", Row(t)[2].Content);
        Assert.Equal("c", Row(t)[4].Content);
        Assert.Equal(new[] { (0, 2), (4, 6) }, Spans(t));
        Assert.True(t.Buffer.HasMultiRowSizedRuns);
    }

    [Fact]
    public void A_block_written_after_a_split_joins_the_span_again()
    {
        var t = Fresh();
        t.Write(Blocks("w=2", "甲乙丙"));
        t.Write($"{Esc}[1;5H  ");

        t.Write($"{Esc}[1;5H" + Sized("w=2", "丁"));

        Assert.Equal("甲乙丁", Text(t));
        Assert.Equal(new[] { (0, 6) }, Spans(t));
    }

    /// <summary>The bytes OpenCode wrote, colours and cursor visibility left out of the first chunk.</summary>
    [Fact]
    public void The_captured_OpenCode_backspace_deletes_one_character()
    {
        var t = Fresh(cols: 80, rows: 24);
        var column = 17;
        foreach (var c in "甲乙中文测试")
        {
            t.Write($"{Esc}[17;{column}H" + Sized("w=2", c.ToString()));
            column += 2;
        }
        Assert.Equal("                甲乙中文测试", Text(t, 16));

        t.Write($"{Esc}[?2026h{Esc}[?25l{Esc}[17;27H"
              + $"{Esc}[38;2;255;255;255m{Esc}[48;2;30;30;30m  "
              + $"{Esc}[0m{Esc}[0m{Esc}[17;27H{Esc}[?25h{Esc}[?2026l");

        Assert.Equal("                甲乙中文测", Text(t, 16));
    }

    [Theory]
    [InlineData("\b\b\u001b[K")]
    [InlineData("\u001b[2D\u001b[K")]
    [InlineData("\u001b[2D\u001b[2X")]
    [InlineData("\u001b[2D\u001b[2P")]
    [InlineData("\u001b[1;5H  ")]
    public void Ordinary_wide_text_deletes_one_character(string delete)
    {
        var t = Fresh();
        t.Write("甲乙丙");

        t.Write(delete);

        Assert.Equal("甲乙", Text(t));
    }
}
