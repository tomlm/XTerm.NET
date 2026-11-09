using XTerm;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// <see cref="Terminal.ContentVersion"/> and <see cref="Terminal.CopyChangedRows"/>: every kind of
/// visible change marks the rows it reached and no others, and a change that touches no cell --
/// a cursor move, an SGR on its own -- marks nothing.
/// </summary>
public class RowChangeTrackingTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static Terminal Make(int cols = 20, int rows = 5) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    /// <summary>The rows that changed after <paramref name="since"/>, as indexes.</summary>
    private static int[] Changed(Terminal t, long since)
    {
        Span<bool> rows = stackalloc bool[t.Rows];
        t.CopyChangedRows(since, rows);
        var result = new List<int>();
        for (int y = 0; y < rows.Length; y++)
        {
            if (rows[y])
                result.Add(y);
        }
        return result.ToArray();
    }

    private static int[] AllRows(Terminal t) => Enumerable.Range(0, t.Rows).ToArray();

    [Fact]
    public void ReadingTwiceWithNothingWritten_DoesNotMove()
    {
        var t = Make();
        var v = t.RefreshContentVersion();

        Assert.Equal(v, t.RefreshContentVersion());
        Assert.Empty(Changed(t, v));
    }

    [Fact]
    public void ContentVersion_IsAPureRead_OfTheLastLook()
    {
        var t = Make();
        var looked = t.RefreshContentVersion();

        t.Write("hello");

        // Reading the property does not look at the buffer, however often it is read.
        Assert.Equal(looked, t.ContentVersion);
        Assert.Equal(looked, t.ContentVersion);

        var after = t.RefreshContentVersion();
        Assert.NotEqual(looked, after);
        Assert.Equal(after, t.ContentVersion);
    }

    [Fact]
    public void ContentVersion_IsZeroBeforeTheFirstLook_WhichMeansEveryRow()
    {
        var t = Make();
        t.Write("x");

        Assert.Equal(0, t.ContentVersion);
        Assert.Equal(AllRows(t), Changed(t, t.ContentVersion));
    }

    [Fact]
    public void TheUsualLoop_CopyChangedRowsThenContentVersion_NeedsNoRefresh()
    {
        var t = Make();
        Span<bool> rows = stackalloc bool[t.Rows];

        long since = t.ContentVersion;
        t.CopyChangedRows(since, rows);
        since = t.ContentVersion;

        t.Write($"{Esc}[4;1Hx");

        Assert.Equal(1, t.CopyChangedRows(since, rows));
        Assert.True(rows[3]);
        since = t.ContentVersion;

        Assert.Equal(0, t.CopyChangedRows(since, rows));
    }

    [Fact]
    public void FirstLook_ReportsEveryRowAsChangedSinceZero()
    {
        var t = Make();

        Assert.Equal(AllRows(t), Changed(t, 0));
    }

    [Fact]
    public void Print_MarksOnlyTheRowWrittenTo()
    {
        var t = Make();
        t.Write($"{Esc}[3;1H");
        var v = t.RefreshContentVersion();

        t.Write("hello");

        Assert.NotEqual(v, t.RefreshContentVersion());
        Assert.Equal([2], Changed(t, v));
    }

    [Fact]
    public void Print_SpanningAWrap_MarksBothRows()
    {
        var t = Make(cols: 10);
        var v = t.RefreshContentVersion();

        t.Write("0123456789abc");

        Assert.Equal([0, 1], Changed(t, v));
    }

    [Fact]
    public void CursorOnlyMoves_DoNotCount()
    {
        var t = Make();
        t.Write("x");
        var v = t.RefreshContentVersion();

        t.Write($"{Esc}[4;7H{Esc}[2A{Esc}[3C\r{Esc}7{Esc}8");

        Assert.Equal(v, t.RefreshContentVersion());
        Assert.Empty(Changed(t, v));
    }

    [Fact]
    public void NoOpWrites_DoNotCount()
    {
        var t = Make();
        t.Write("x");
        var v = t.RefreshContentVersion();

        // Nothing at all, and an SGR on its own: the pen changes, no cell does.
        t.Write("");
        t.Write(ReadOnlySpan<byte>.Empty);
        t.Write($"{Esc}[1;31m");

        Assert.Equal(v, t.RefreshContentVersion());
    }

    [Fact]
    public void AttributeOnlyChange_Counts()
    {
        var t = Make();
        t.Write("a");
        var v = t.RefreshContentVersion();

        // The same character back in the same cell, in red: only its attributes differ.
        t.Write($"\r{Esc}[31ma");

        Assert.Equal([0], Changed(t, v));
    }

    [Fact]
    public void EraseInLine_MarksThatRow()
    {
        var t = Make();
        t.Write("one\r\ntwo\r\nthree");
        var v = t.RefreshContentVersion();

        t.Write($"{Esc}[2;1H{Esc}[K");

        Assert.Equal([1], Changed(t, v));
    }

    [Fact]
    public void EraseCharacters_MarksThatRow()
    {
        var t = Make();
        t.Write("one\r\ntwo\r\nthree");
        var v = t.RefreshContentVersion();

        t.Write($"{Esc}[3;2H{Esc}[2X");

        Assert.Equal([2], Changed(t, v));
    }

    [Fact]
    public void ClearScreen_MarksEveryRow()
    {
        var t = Make();
        t.Write("one\r\ntwo");
        var v = t.RefreshContentVersion();

        t.Write($"{Esc}[2J");

        Assert.Equal(AllRows(t), Changed(t, v));
    }

    [Fact]
    public void LineFeedAtTheBottom_ScrollsAndMarksEveryRow()
    {
        var t = Make(rows: 4);
        t.Write("a\r\nb\r\nc\r\nd");
        var v = t.RefreshContentVersion();

        // No cell is written by the scroll itself: every row now shows a different line.
        t.Write("\n");

        Assert.Equal(AllRows(t), Changed(t, v));
    }

    [Fact]
    public void ScrollUp_MarksEveryRowInTheRegion()
    {
        var t = Make(rows: 4);
        t.Write("a\r\nb\r\nc\r\nd");
        var v = t.RefreshContentVersion();

        t.Write($"{Esc}[S");

        Assert.Equal(AllRows(t), Changed(t, v));
    }

    [Fact]
    public void ScrollWithFullScrollback_StillMarksEveryRow()
    {
        // Scrollback 0 recycles the top line to the bottom instead of allocating one.
        var t = new Terminal(new TerminalOptions { Cols = 10, Rows = 3, Scrollback = 0 });
        t.Write("a\r\nb\r\nc");
        var v = t.RefreshContentVersion();

        t.Write("\n");

        Assert.Equal(AllRows(t), Changed(t, v));
    }

    [Fact]
    public void ViewportScroll_MarksEveryRow()
    {
        var t = Make(rows: 3);
        t.Write("1\r\n2\r\n3\r\n4\r\n5\r\n6");
        var v = t.RefreshContentVersion();

        t.ScrollLines(-1);

        Assert.Equal(AllRows(t), Changed(t, v));
    }

    [Fact]
    public void Resize_MarksEveryRowOfTheNewSize()
    {
        var t = Make(rows: 5);
        t.Write("x");
        var v = t.RefreshContentVersion();

        t.Resize(20, 7);

        Assert.Equal(AllRows(t), Changed(t, v));
        Assert.Equal(7, Changed(t, v).Length);
    }

    [Fact]
    public void AlternateScreenSwitch_MarksEveryRow_BothWays()
    {
        var t = Make();
        t.Write("main");
        var v = t.RefreshContentVersion();

        t.Write($"{Esc}[?1049h");
        Assert.Equal(AllRows(t), Changed(t, v));

        v = t.RefreshContentVersion();
        t.Write($"{Esc}[?1049l");
        Assert.Equal(AllRows(t), Changed(t, v));
    }

    private static string Sized(string metadata, string text) => $"{Esc}]66;{metadata};{text}{Esc}\\";

    [Fact]
    public void SizedText_MarksEveryRowItCovers()
    {
        // Stored on row 0's line alone, drawn over rows 0-2.
        var t = Make();
        var v = t.RefreshContentVersion();

        t.Write(Sized("s=3", "X"));

        Assert.Equal([0, 1, 2], Changed(t, v));
    }

    [Fact]
    public void ErasingSizedText_MarksEveryRowItCovered()
    {
        var t = Make();
        t.Write(Sized("s=3", "X"));
        var v = t.RefreshContentVersion();

        // The run is gone, so only what it USED to cover can say rows 1-2 changed.
        t.Write($"\r{Esc}[K");

        Assert.Equal([0, 1, 2], Changed(t, v));
    }

    [Fact]
    public void SizedText_AnchoredAboveTheViewport_MarksTheRowsItReachesInto()
    {
        var t = Make(rows: 5);
        t.Write(Sized("s=3", "X"));
        t.Write("\r\n\r\n\r\n\r\n\r\n");   // one scroll: the anchor is now just above the top row
        Assert.Equal(1, t.Buffer.ViewportY);
        var v = t.RefreshContentVersion();

        t.Buffer.GetLine(0)!.Fill(XTerm.Buffer.BufferCell.Space);

        Assert.Equal([0, 1], Changed(t, v));
    }

    [Fact]
    public void ZeroRowTerminal_CanBeObservedBeforeLayout_ThenResized()
    {
        // A host constructs at zero rows before layout, which the constructor accepts.
        var t = new Terminal(new TerminalOptions { Cols = 20, Rows = 0 });

        var v = t.RefreshContentVersion();
        Assert.Equal(0, t.CopyChangedRows(v, Span<bool>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.GetRowVersion(0));

        t.Resize(20, 5);

        Assert.Equal([0, 1, 2, 3, 4], Changed(t, v));
    }

    [Fact]
    public void RowVersion_IsTheVersionTheRowLastChangedAt()
    {
        var t = Make();
        var before = t.RefreshContentVersion();

        t.Write($"{Esc}[2;1Hx");
        var after = t.RefreshContentVersion();

        Assert.Equal(after, t.GetRowVersion(1));
        Assert.True(t.GetRowVersion(0) <= before);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.GetRowVersion(t.Rows));
    }

    [Fact]
    public void TwoObservers_EachSeeTheirOwnChanges()
    {
        var t = Make();
        var first = t.RefreshContentVersion();

        t.Write("a");
        var second = t.RefreshContentVersion();
        t.Write($"{Esc}[3;1Hb");

        // The second observer looked between the writes; the first did not.
        Assert.Equal([0, 2], Changed(t, first));
        Assert.Equal([2], Changed(t, second));
    }

    [Fact]
    public void CopyChangedRows_ReturnsTheCount_AndClearsPastTheGrid()
    {
        var t = Make(rows: 3);
        var v = t.RefreshContentVersion();
        t.Write("a");

        Span<bool> rows = [true, true, true, true, true];
        var count = t.CopyChangedRows(v, rows);

        Assert.Equal(1, count);
        Assert.Equal(new[] { true, false, false, false, false }, rows.ToArray());
    }
}
