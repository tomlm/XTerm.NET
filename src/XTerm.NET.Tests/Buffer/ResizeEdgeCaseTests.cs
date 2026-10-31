using XTerm;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests.Buffer;

/// <summary>
/// Resize and reflow edge cases: one-column terminals, buffers at capacity, and zero-row buffers.
/// </summary>
/// <remarks>
/// Every one of these failed before the fix that accompanies it, and each failed LOUDLY -- two
/// IndexOutOfRangeException, one OutOfMemoryException after a hang, one out-of-bounds cursor, one
/// viewport pointing at rows the user cannot see, and one buffer that could never be written to.
/// None is exotic: they are what a one-column pane, a full scrollback, or a shrinking window
/// produce on their own.
/// </remarks>
public class ResizeEdgeCaseTests
{
    private static void SetCell(BufferLine line, int col, string content, int width = 1)
    {
        var cell = new BufferCell(content, width, AttributeData.Default);
        line.SetCell(col, ref cell);
    }

    private static void SetWideCell(BufferLine line, int col, string content)
    {
        SetCell(line, col, content, 2);
        SetCell(line, col + 1, "", 0);
    }

    // One-column reflow with a wide boundary
    [Fact]
    public void ShrinkToOneColumn_WithWideChars_Terminates()
    {
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.Resize(6, 10);
        buffer.SetCursorRaw(0, 5);

        for (var col = 0; col < 6; col += 2)
        {
            SetWideCell(buffer.Lines[0]!, col, "漢");
            SetWideCell(buffer.Lines[1]!, col, "漢");
        }

        buffer.Lines[1]!.IsWrapped = true;

        // Hung, then threw OutOfMemoryException: a zero-length row made no progress, so the loop
        // appended rows until the list could not grow.
        buffer.Resize(1, 10);
    }

    // The viewport adjustment pops rows the outer loop is still walking
    [Fact]
    public void ShrinkWithFullLastRow_AndCursorAtTop_DoesNotThrow()
    {
        // Constructed at 10x10 directly. Resizing an 80x24 buffer down leaves 24 LINES, not 10, so
        // the last row is not row 9 and the Pop path never runs -- which is why the first attempt at
        // this probe passed while proving nothing.
        var buffer = new TerminalBuffer(10, 10, 1000);
        buffer.SetCursorRaw(0, 0);

        for (var col = 0; col < 10; col++)
        {
            SetCell(buffer.Lines[9]!, col, "x");
        }

        var ex = Record.Exception(() => buffer.Resize(2, 10));
        Assert.Null(ex);
    }

    // A negative cursor must not survive a resize
    [Fact]
    public void NegativeCursor_IsNotPreservedAcrossResize()
    {
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.Resize(20, 10);

        buffer.SetCursorRaw(-5, -5);
        buffer.Resize(10, 5);

        Assert.True(buffer.X >= 0, $"X was {buffer.X}");
        Assert.True(buffer.Y >= 0, $"Y was {buffer.Y}");
    }

    // A line expanding past the remaining capacity
    [Fact]
    public void ExpansionBeyondCapacity_DoesNotThrow()
    {
        var buffer = new TerminalBuffer(80, 2, 1);
        buffer.SetCursorRaw(0, 0);

        for (var col = 0; col < 80; col++)
        {
            SetCell(buffer.Lines[1]!, col, "x");
        }

        var ex = Record.Exception(() => buffer.Resize(2, 2));
        Assert.Null(ex);
    }

    // The viewport after a capacity trim
    [Fact]
    public void ViewportFollowsTheBottom_AfterCapacityTrim()
    {
        // Fill past capacity so the buffer is at MaxLength and following the bottom, then shrink the
        // row count enough that capacity has to trim.
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5, Scrollback = 5 });
        for (var i = 0; i < 12; i++)
        {
            terminal.Write($"line{i}\r\n");
        }

        var before = terminal.Buffer;
        Assert.Equal(before.YBase, before.YDisp);

        terminal.Resize(20, 3);

        var after = terminal.Buffer;
        Assert.Equal(
            after.Lines.Length - 3,
            after.YBase);
    }

    // A zero-row buffer brought to life by a later resize
    [Fact]
    public void ZeroRowBuffer_IsUsableAfterResize()
    {
        var buffer = new TerminalBuffer(80, 0, 1000);

        buffer.Resize(80, 24);

        Assert.True(buffer.Lines.Length > 0, $"Lines.Length was {buffer.Lines.Length}");
    }

    /// <summary>
    /// A shrink that was following the tail keeps following it, with nothing stranded below.
    /// </summary>
    /// <remarks>
    /// The screen is the last `rows` lines of the buffer, so a shrink has to move the difference
    /// into scrollback. Shifting only far enough to bring the cursor back on screen left lines below
    /// the screen — and the viewport tops out at YBase, so scrolling could never reach them again.
    /// Caught in review on this PR.
    /// </remarks>
    [Fact]
    public void ShrinkingRows_LeavesNothingStrandedBelowTheScreen()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        // Park the cursor with blank rows below it, which is what leaves room to strand.
        terminal.Write("\u001b[24;1H");
        terminal.Write("\u001b[6A");

        var contentRow = terminal.Buffer.YBase + terminal.Buffer.Y;

        terminal.Resize(40, 10);

        // The cursor is still on its line...
        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);

        // ...and the screen reaches the end of the buffer, so nothing is below it.
        Assert.Equal(terminal.Buffer.Lines.Length, terminal.Buffer.YBase + terminal.Rows);

        // A viewport that was at the tail is still at the tail.
        Assert.Equal(terminal.Buffer.YBase, terminal.Buffer.ViewportY);
    }

    /// <summary>
    /// A fresh shell's cursor sits near the top. Shrinking must drop the blank rows below it, not
    /// leave them below the screen.
    /// </summary>
    /// <remarks>
    /// Every emulator starts at 24 rows and is resized to its layout. Lines were moved out only
    /// when the cursor fell below the new bottom, so a shrink with the cursor at the top kept all 24
    /// lines with YBase at 0 -- and ScrollUp appends at the tail, so the extra lines lasted for the
    /// life of the terminal: a buffer longer than its scrollback, and a scrollbar with nothing to
    /// scroll.
    /// </remarks>
    [Fact]
    public void ShrinkingRows_WithCursorAtTop_PopsTheBlankRowsBelowIt()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        terminal.Write("banner\r\nprompt$ ");

        terminal.Resize(40, 12);

        Assert.Equal(12, terminal.Buffer.Lines.Length);
        Assert.Equal(0, terminal.Buffer.YBase);
        Assert.Equal(1, terminal.Buffer.Y);
        Assert.True(terminal.Buffer.ViewportY <= terminal.Buffer.YBase);

        // Scrolling afterwards keeps the buffer exactly scrollback + screen.
        for (var i = 0; i < 30; i++)
            terminal.Write($"\r\nline {i}");
        Assert.Equal(terminal.Buffer.Lines.Length, terminal.Buffer.YBase + terminal.Rows);
        Assert.Equal(terminal.Buffer.YBase, terminal.Buffer.ViewportY);
    }

    /// <summary>
    /// With content all the way down to the cursor on the last row, there is nothing below it to
    /// pop: the shrink moves the top of the screen into scrollback, as it always did.
    /// </summary>
    [Fact]
    public void ShrinkingRows_WithCursorOnTheBottomRow_ScrollsIntoScrollback()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 23; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        terminal.Resize(40, 10);

        Assert.Equal(24, terminal.Buffer.Lines.Length);
        Assert.Equal(14, terminal.Buffer.YBase);
        Assert.Equal(9, terminal.Buffer.Y);
        Assert.Equal(14, terminal.Buffer.ViewportY);
        Assert.True(terminal.Buffer.ViewportY <= terminal.Buffer.YBase);
        for (var i = 0; i < 23; i++)
            Assert.Equal($"line {i}", terminal.Buffer.Lines[i]!.TranslateToString(true));
    }

    /// <summary>
    /// The cursor in the middle with blank rows below it: those are popped first, then the rest
    /// scrolls into scrollback.
    /// </summary>
    [Fact]
    public void ShrinkingRows_WithCursorInTheMiddle_PopsBelowThenScrolls()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 10; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        // 13 blank rows below the cursor on row 10. Shrinking by 19 pops those 13, then scrolls 6.
        terminal.Resize(40, 5);

        Assert.Equal(11, terminal.Buffer.Lines.Length);
        Assert.Equal(6, terminal.Buffer.YBase);
        Assert.Equal(4, terminal.Buffer.Y);
        Assert.Equal(6, terminal.Buffer.ViewportY);
        Assert.True(terminal.Buffer.ViewportY <= terminal.Buffer.YBase);
        for (var i = 0; i < 10; i++)
            Assert.Equal($"line {i}", terminal.Buffer.Lines[i]!.TranslateToString(true));
        Assert.Equal("prompt$", terminal.Buffer.Lines[10]!.TranslateToString(true));
    }

    /// <summary>
    /// ViewportY never ends up past YBase after a shrink, wherever the cursor is and whether or not
    /// the viewport was scrolled up into scrollback. A host derives its scroll range from YBase, so
    /// a viewport beyond it would point below the screen.
    /// </summary>
    /// <remarks>
    /// The screen is full of output here, so the cursor rows exercise all three outcomes: on row 24
    /// nothing is below it and nothing is lost; on rows 1 and 12 the content below the cursor is
    /// more than a 10-row screen can hold once the cursor reaches the top, and only the lines that
    /// cannot fit are dropped. Every line that remains is the line that was there.
    /// </remarks>
    [Theory]
    [InlineData(1, false)]
    [InlineData(12, false)]
    [InlineData(24, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    [InlineData(24, true)]
    public void ShrinkingRows_KeepsTheViewportWithinScrollback(int cursorRow, bool scrolledUp)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 40; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write($"\u001b[{cursorRow};1H");
        if (scrolledUp)
            terminal.ScrollLines(-10);

        var buffer = terminal.Buffer;
        var contentRow = buffer.YBase + buffer.Y;
        Assert.Equal(scrolledUp, buffer.ViewportY < buffer.YBase);
        var before = new string[buffer.Lines.Length];
        for (var i = 0; i < before.Length; i++)
            before[i] = buffer.Lines[i]!.TranslateToString(true);

        terminal.Resize(40, 10);

        Assert.InRange(buffer.ViewportY, 0, buffer.YBase);
        Assert.Equal(buffer.Lines.Length, buffer.YBase + terminal.Rows);
        Assert.Equal(contentRow, buffer.YBase + buffer.Y);
        // A viewport that was following the tail still is; one scrolled up stays scrolled up.
        Assert.Equal(scrolledUp, buffer.ViewportY < buffer.YBase);

        // Nothing is dropped that a 10-row screen below the cursor's line could hold...
        Assert.Equal(Math.Min(before.Length, contentRow + terminal.Rows), buffer.Lines.Length);
        // ...and every line kept is the line that was there.
        for (var i = 0; i < buffer.Lines.Length; i++)
            Assert.Equal(before[i], buffer.Lines[i]!.TranslateToString(true));
    }

    /// <summary>
    /// Content below the cursor survives a shrink and comes back when the window grows: a progress
    /// bar redrawn above earlier output, an fzf --height list, a DECSDM Sixel.
    /// </summary>
    /// <remarks>
    /// Popping every line below the cursor, as xterm.js does, fixed the blank-row strand but made
    /// this case lossy, where it had been lossless: rows 19 to 23 came back blank. Found in review.
    /// </remarks>
    [Fact]
    public void ShrinkThenGrow_WithTheCursorAboveContent_KeepsTheContent()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 23; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");
        terminal.Write("\u001b[5A");
        Assert.Equal(18, terminal.Buffer.Y);

        terminal.Resize(40, 10);

        Assert.Equal(24, terminal.Buffer.Lines.Length);
        Assert.Equal(14, terminal.Buffer.YBase);
        Assert.Equal(4, terminal.Buffer.Y);

        terminal.Resize(40, 24);

        Assert.Equal(0, terminal.Buffer.YBase);
        Assert.Equal(18, terminal.Buffer.Y);
        for (var i = 0; i < 23; i++)
            Assert.Equal($"line {i}", terminal.Buffer.Lines[i]!.TranslateToString(true));
        Assert.Equal("prompt$", terminal.Buffer.Lines[23]!.TranslateToString(true));
    }

    /// <summary>
    /// Shrinking and growing back with the cursor near the top and output below it: only the blank
    /// rows go, so the shrunk buffer is exactly the screen, and the output is still there after.
    /// </summary>
    [Fact]
    public void ShrinkingThenGrowingRows_WithCursorAtTop_RoundTrips()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        terminal.Write("banner\r\nprompt$ ");
        for (var row = 7; row <= 9; row++)
            terminal.Write($"\u001b[{row};1Hitem {row}");
        terminal.Write("\u001b[2;9H");

        terminal.Resize(40, 12);

        // Lines 9 to 23 were blank: 12 of them are popped, and nothing is left below the screen.
        Assert.Equal(12, terminal.Buffer.Lines.Length);
        Assert.Equal(0, terminal.Buffer.YBase);
        Assert.Equal(1, terminal.Buffer.Y);

        terminal.Resize(40, 24);

        Assert.Equal(24, terminal.Buffer.Lines.Length);
        Assert.Equal(0, terminal.Buffer.YBase);
        Assert.Equal(1, terminal.Buffer.Y);
        Assert.Equal("banner", terminal.Buffer.Lines[0]!.TranslateToString(true));
        Assert.Equal("prompt$", terminal.Buffer.Lines[1]!.TranslateToString(true));
        for (var row = 7; row <= 9; row++)
            Assert.Equal($"item {row}", terminal.Buffer.Lines[row - 1]!.TranslateToString(true));
    }

    /// <summary>
    /// The alternate buffer has no scrollback, and both buffers are resized together: shrinking
    /// while a full-screen app is up leaves neither holding more lines than the screen.
    /// </summary>
    [Fact]
    public void ShrinkingRows_InTheAlternateBuffer_LeavesBothBuffersAtScreenHeight()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        terminal.Write("prompt$ ");
        terminal.Write("\u001b[?1049h");
        terminal.Write("\u001b[3;1Hmenu");

        terminal.Resize(40, 12);

        Assert.Equal(BufferType.Alternate, terminal.ActiveBuffer);
        Assert.Equal(12, terminal.Buffer.Lines.Length);
        Assert.Equal(0, terminal.Buffer.YBase);
        Assert.Equal("menu", terminal.Buffer.Lines[2]!.TranslateToString(true));

        terminal.Write("\u001b[?1049l");
        Assert.Equal(12, terminal.Buffer.Lines.Length);
        Assert.Equal(0, terminal.Buffer.YBase);
    }

    /// <summary>
    /// A zero-row resize has no viewport to overflow out of, and must not scroll the buffer.
    /// </summary>
    /// <remarks>
    /// The bottom row of a zero-row viewport is not -1, and treating it as such makes the overflow
    /// one line too large -- so a resize that shows nothing still moved the cursor's content row,
    /// and the line that came back at the top when rows were restored was the wrong one. Zero rows
    /// is a real case here: a buffer can be built with none and brought to life by a later resize.
    /// </remarks>
    [Fact]
    public void ZeroRowResize_DoesNotScrollTheBuffer()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        var contentRow = terminal.Buffer.YBase + terminal.Buffer.Y;

        terminal.Resize(40, 0);
        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);

        terminal.Resize(40, 24);
        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);
    }

    /// <summary>
    /// A resize must not move the cursor off the line it is on. Its position is YBase + Y, and both
    /// halves of a resize used to change one without the other.
    /// </summary>
    /// <remarks>
    /// <para>The consequence is silent corruption rather than a crash, which is why it survived: the
    /// cursor lands on earlier content and the next write destroys a line the application never
    /// touched. A shell hides its own damage, because it redraws its prompt on every SIGWINCH and
    /// repaints what it just overwrote. Anything that does NOT repaint -- a Sixel picture, a
    /// full-screen TUI mid-frame -- keeps the evidence.</para>
    /// <para>Both directions are tested, because they fail through different mechanisms and fixing
    /// one leaves the other.</para>
    /// </remarks>
    [Fact]
    public void ShrinkingRows_KeepsTheCursorOnItsLine()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        var contentRow = terminal.Buffer.YBase + terminal.Buffer.Y;

        terminal.Resize(40, 8);

        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);
    }

    [Fact]
    public void GrowingRows_KeepsTheCursorOnItsLine()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        terminal.Resize(40, 8);
        var contentRow = terminal.Buffer.YBase + terminal.Buffer.Y;

        terminal.Resize(40, 24);

        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);
    }

    /// <summary>
    /// The live case: a drag is many resize events, and a shell writes between them. What the cursor
    /// slides over is what gets destroyed, so the round trip is asserted on CONTENT and not only on
    /// coordinates.
    /// </summary>
    [Fact]
    public void ResizeLadderWithRedraws_LeavesEarlierLinesIntact()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        for (var rows = 20; rows >= 6; rows -= 4)
        {
            terminal.Resize(40, rows);
            terminal.Write("\rprompt$ ");
        }

        for (var rows = 10; rows <= 24; rows += 4)
        {
            terminal.Resize(40, rows);
            terminal.Write("\rprompt$ ");
        }

        // Every "line N" written before the drag must still read back exactly.
        for (var i = 0; i < 20; i++)
        {
            var line = terminal.Buffer.Lines[i];
            Assert.NotNull(line);
            Assert.Equal($"line {i}", line!.TranslateToString(true).TrimEnd());
        }
    }
}
