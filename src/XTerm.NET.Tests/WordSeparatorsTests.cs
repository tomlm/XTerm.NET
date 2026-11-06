using XTerm;
using XTerm.Options;
using XTerm.Selection;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// <see cref="TerminalOptions.WordSeparators"/>: where a double-click word ends. Null keeps the
/// built-in rule; a string lists the separators, everything else being part of a word.
/// </summary>
public class WordSeparatorsTests
{
    private static Terminal Write(string text, string? separators = null)
    {
        var t = new Terminal(new TerminalOptions { Cols = 40, Rows = 3, WordSeparators = separators });
        t.Write(text);
        return t;
    }

    private static string DoubleClick(Terminal t, int col)
    {
        t.Selection.StartSelection(col, 0, SelectionMode.Word);
        return t.Selection.GetSelectionText();
    }

    [Fact]
    public void By_default_punctuation_ends_a_word()
    {
        var t = Write("cat ~/src/foo-bar.txt");
        Assert.Equal("foo", DoubleClick(t, 11));
    }

    [Fact]
    public void Separators_listed_end_a_word_and_nothing_else_does()
    {
        var t = Write("cat ~/src/foo-bar.txt", " ");
        Assert.Equal("~/src/foo-bar.txt", DoubleClick(t, 11));
    }

    [Fact]
    public void Ghosttys_default_list_keeps_a_path_whole_and_stops_at_a_colon()
    {
        var t = Write("at src/foo.cs:42 here", "\t '\"│`|:;,()[]{}<>$");
        Assert.Equal("src/foo.cs", DoubleClick(t, 5));
    }

    [Fact]
    public void A_separator_outside_the_basic_plane_counts_as_one_code_point()
    {
        var t = Write("ab\U0001F600cd", "\U0001F600");
        Assert.Equal("ab", DoubleClick(t, 0));
    }

    /// <summary>
    /// A wide glyph is two cells, the second a width-0 continuation with no content. Expansion walks the
    /// glyph whole, from either side and from a click on either half.
    /// </summary>
    [Theory]
    [InlineData("a\U0001F600b", 0)]
    [InlineData("a\U0001F600b", 3)]
    [InlineData("a\U0001F600b", 2)]
    [InlineData("a\u7532b", 0)]
    [InlineData("a\u7532b", 3)]
    public void A_wide_character_that_is_not_a_separator_stays_inside_the_word(string text, int col)
    {
        var t = Write(text, " ");
        Assert.Equal(text, DoubleClick(t, col));
    }

    [Fact]
    public void A_wide_letter_stays_inside_the_word_under_the_default_rule_too()
    {
        // The built-in rule counts a CJK letter as a word character; its continuation cell has to agree.
        var t = Write("a\u7532b");
        Assert.Equal("a\u7532b", DoubleClick(t, 0));
    }

    [Fact]
    public void A_wide_separator_still_ends_the_word()
    {
        // The space too, so the blank cells after "cd" end it on the right; the emoji ends it on the left.
        var t = Write("ab\U0001F600cd", " \U0001F600");
        Assert.Equal("cd", DoubleClick(t, 4));
    }

    [Fact]
    public void A_wide_separators_second_cell_is_not_taken_into_the_word_beside_it()
    {
        // Its continuation adds no text, so only the range shows it: the word starts at "c", not one
        // cell earlier on the separator's second half.
        var t = Write("ab\U0001F600cd", " \U0001F600");
        t.Selection.StartSelection(4, 0, SelectionMode.Word);

        Assert.True(t.Selection.TryGetSelection(out var range));
        Assert.Equal(4, range.StartX);
    }

    [Fact]
    public void An_empty_cell_that_follows_no_wide_glyph_still_ends_a_word()
    {
        // Width 0 and no content, but not a continuation: nothing wide comes before it.
        var t = Write("ab cd", "");
        t.Buffer.Lines[0]![2] = XTerm.Buffer.BufferCell.Empty;

        Assert.Equal("ab", DoubleClick(t, 0).TrimEnd());
    }

    /// <summary>
    /// Blank cells hold a space, not nothing, so a gap the cursor skipped is spaces like any other: a
    /// list without the space joins across it, and one with it stops there. Ghostty's default has it.
    /// </summary>
    [Fact]
    public void Blank_cells_are_spaces_so_a_list_needs_the_space_to_stop_at_a_gap()
    {
        var joined = Write("one", "");
        joined.Write("\u001b[1;6Htwo");
        Assert.StartsWith("one  two", DoubleClick(joined, 1));

        var stopped = Write("one", " ");
        stopped.Write("\u001b[1;6Htwo");
        Assert.Equal("one", DoubleClick(stopped, 1));
    }

    [Fact]
    public void A_change_applies_to_the_next_double_click()
    {
        var t = Write("foo-bar");
        Assert.Equal("foo", DoubleClick(t, 1));

        t.Options.WordSeparators = " ";
        Assert.Equal("foo-bar", DoubleClick(t, 1));

        // One list to another, not only to and from null: the parsed set has to follow the string.
        t.Options.WordSeparators = "-";
        Assert.Equal("foo", DoubleClick(t, 1));

        t.Options.WordSeparators = null;
        Assert.Equal("foo", DoubleClick(t, 1));
    }

    [Fact]
    public void The_rule_is_public_for_a_host_to_share()
    {
        var t = Write("", "-");
        Assert.True(t.Selection.IsWordChar("."));
        Assert.False(t.Selection.IsWordChar("-"));
        Assert.False(t.Selection.IsWordChar(""));
        Assert.False(t.Selection.IsWordChar(null));
    }

    [Fact]
    public void A_copy_of_the_options_keeps_it()
    {
        var options = new TerminalOptions { WordSeparators = " :" };
        Assert.Equal(" :", options.Clone().WordSeparators);
    }
}
