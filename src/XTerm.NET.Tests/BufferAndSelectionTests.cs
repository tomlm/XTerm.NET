using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Buffer bookkeeping and selection: what breaks when a row index, a wrap flag or a trimmed line
/// is tracked slightly wrong.
/// </summary>
public class BufferAndSelectionTests
{
    private static Terminal NewTerminal(int cols = 10, int rows = 4, int scrollback = 10) =>
        new(new TerminalOptions { Cols = cols, Rows = rows, Scrollback = scrollback });

    [Fact]
    public void Copying_wrapped_text_does_not_break_it_at_the_wrap()
    {
        // IsWrapped means "this line continues the previous one", so whether row y joins row y+1
        // is row y+1's answer. Testing row y put newlines inside wrapped text.
        var terminal = NewTerminal(cols: 10);
        terminal.Write("abcdefghijKLMNO");     // wraps after 10 columns

        terminal.Selection.StartSelection(0, 0);
        terminal.Selection.UpdateSelection(4, 1);
        var text = terminal.Selection.GetSelectionText();

        Assert.DoesNotContain("\n", text);
        Assert.StartsWith("abcdefghij", text);
    }

    [Fact]
    public void Separate_lines_keep_their_newline()
    {
        var terminal = NewTerminal(cols: 10);
        terminal.Write("one\r\ntwo");

        terminal.Selection.StartSelection(0, 0);
        terminal.Selection.UpdateSelection(2, 1);

        Assert.Contains("\n", terminal.Selection.GetSelectionText());
    }

    [Fact]
    public void Clearing_the_scrollback_reports_the_rows_it_dropped()
    {
        // Anything tracking absolute rows -- a selection, a search hit, a shell-integration mark --
        // is left pointing above the buffer otherwise.
        var terminal = NewTerminal();
        var trimmed = 0;
        terminal.Buffer.Trimmed += n => trimmed += n;

        for (var i = 0; i < 8; i++)
            terminal.Write($"line {i}\r\n");

        var before = terminal.Buffer.YBase;
        Assert.True(before > 0, "test needs scrollback to have accumulated");

        terminal.Buffer.ClearScrollback();

        Assert.Equal(before, trimmed);
        Assert.Equal(0, terminal.Buffer.YBase);
    }

    [Fact]
    public void GetLine_returns_null_for_a_row_that_is_not_there()
    {
        // The signature promises a nullable; it threw instead, so a caller written against the
        // contract was one stale row index away from taking down the write loop.
        var terminal = NewTerminal();

        Assert.Null(terminal.Buffer.GetLine(-1));
        Assert.Null(terminal.Buffer.GetLine(10_000));
    }

    [Fact]
    public void Trimming_releases_the_lines_it_drops()
    {
        // Advancing the start index alone kept every trimmed line referenced until a later push
        // happened to overwrite that slot.
        var list = new XTerm.Buffer.CircularList<string>(4);
        list.Push("a"); list.Push("b"); list.Push("c"); list.Push("d");

        list.TrimStart(2);

        Assert.Equal(2, list.Length);
        Assert.Equal("c", list[0]);
        Assert.Equal("d", list[1]);
    }

    private static string[] Contents<T>(XTerm.Buffer.CircularList<T> list) where T : class =>
        Enumerable.Range(0, list.Length).Select(i => list[i]!.ToString()!).ToArray();

    private static XTerm.Buffer.CircularList<string> Abcd()
    {
        var list = new XTerm.Buffer.CircularList<string>(4);
        list.Push("a"); list.Push("b"); list.Push("c"); list.Push("d");
        return list;
    }

    [Fact]
    public void Splicing_into_a_full_list_inserts_where_it_was_asked_to()
    {
        // At capacity the insert became an append, so a reflowed line landed at the bottom of the
        // scrollback instead of where its text was.
        var list = Abcd();

        list.Splice(1, 0, "X");

        // The same splice with room to grow gives a,X,b,c,d. At capacity the oldest falls off the
        // front, which is 'a' -- X still lands immediately before the 'b' it was inserted ahead of.
        Assert.Equal(["X", "b", "c", "d"], Contents(list));
    }

    [Fact]
    public void Splicing_a_full_list_agrees_with_the_same_splice_that_had_room()
    {
        // The eviction is the ONLY difference capacity is allowed to make. Anything else means
        // the at-capacity path has drifted from the semantics of the branch beside it.
        var roomy = new XTerm.Buffer.CircularList<string>(5);
        roomy.Push("a"); roomy.Push("b"); roomy.Push("c"); roomy.Push("d");
        roomy.Splice(1, 0, "X");

        var full = Abcd();
        full.Splice(1, 0, "X");

        Assert.Equal(Contents(roomy).Skip(1).ToArray(), Contents(full));
    }

    [Fact]
    public void Splicing_several_items_into_a_full_list_keeps_them_together_and_in_order()
    {
        // Advancing `start` per item while each insert also evicted one moved the target twice
        // per item, interleaving the inserted run with the rows it was supposed to precede.
        var list = Abcd();

        list.Splice(2, 0, "X", "Y");

        Assert.Equal(["X", "Y", "c", "d"], Contents(list));
    }

    [Fact]
    public void Splicing_at_the_front_of_a_full_list_drops_the_item_that_scrolls_off()
    {
        // Inserted at index 0 of a full list, the new item is itself the oldest, so it falls off
        // in the same breath. Pushing it displaced a live row instead.
        var list = Abcd();

        list.Splice(0, 0, "X");

        Assert.Equal(["a", "b", "c", "d"], Contents(list));
    }

    [Fact]
    public void Splicing_at_the_end_of_a_full_list_appends_and_drops_the_oldest()
    {
        var list = Abcd();

        list.Splice(4, 0, "X");

        Assert.Equal(["b", "c", "d", "X"], Contents(list));
    }
}
