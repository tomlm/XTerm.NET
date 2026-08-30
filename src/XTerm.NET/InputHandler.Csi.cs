using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Input;
using XTerm.Parser;

namespace XTerm;

/// <summary>
/// Handles input escape sequences and updates the terminal buffer.
/// Implements VT100/xterm escape sequence handlers.
/// </summary>
public partial class InputHandler
{
    /// <summary>
    /// The current background, without foreground or text rendition attributes. This mirrors
    /// xterm.js <c>_eraseAttrData()</c>: BCE paints a background, while a later write supplies its
    /// own foreground and rendition.
    /// </summary>
    private AttributeData GetEraseAttributes()
    {
        var attributes = AttributeData.Default;
        attributes.Bg = _curAttr.Bg;
        return attributes;
    }

    /// <summary>
    /// Records the character just printed, and where it left the cursor, for <c>REP</c>.
    /// </summary>
    /// <remarks>
    /// <para>Two things guard the record, and they catch different intrusions. Any control, CSI,
    /// ESC, OSC or DCS between the print and the REP FORGETS it, cleared at the dispatch points
    /// rather than from every handler -- which is how xterm.js does it (its parser zeroes
    /// <c>precedingJoinState</c> after every dispatch), and is the version that does not rot,
    /// because a new handler cannot forget to cancel what the dispatcher already cancelled.
    /// That is what catches a cursor moved away and back: the position afterwards is the same,
    /// but a sequence intervened, so there is no preceding character any more.</para>
    /// <para>The stored position is the second guard, for movement that arrives through no
    /// sequence at all -- the host calling a public cursor API between writes.</para>
    /// <para>REP itself is exempt from the clearing, so a chain of REPs keeps repeating: after a
    /// REP the character it printed IS the preceding graphic character (ECMA-48's reading).
    /// xterm.js happens to cancel there, but only because its parser clears after the handler
    /// returns; nothing in the spec asks for that.</para>
    /// </remarks>
    private void RememberForRepeat(int codePoint, int clusterId)
    {
        _lastPrinted = (_buffer.Y + _buffer.YBase, _buffer.X, codePoint, clusterId, codePoint);
    }

    /// <summary>Forgets the preceding character. See <see cref="RememberForRepeat"/> for when.</summary>
    internal void CancelRepeat() => _lastPrinted = null;

    private void HandleDecLineAttribute(string finalChar)
    {
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null) return;
        switch (finalChar)
        {
            case "3": line.LineAttribute = LineAttribute.DoubleHeightTop; break;
            case "4": line.LineAttribute = LineAttribute.DoubleHeightBottom; break;
            case "5": line.LineAttribute = LineAttribute.Normal; break;
            case "6": line.LineAttribute = LineAttribute.DoubleWidth; break;
            case "8": FillScreenWithE(); break;
        }
    }

    private void FillScreenWithE()
    {
        var cell = new BufferCell('E', 1, AttributeData.Default);
        for (int row = 0; row < _terminal.Rows; row++)
        {
            var line = _buffer.Lines[_buffer.YBase + row];
            if (line != null)
            {
                line.LineAttribute = LineAttribute.Normal;
                line.Fill(cell);
            }
        }
        _buffer.SetCursor(0, 0);
    }

    /// <summary>
    /// Serialises the current character attributes as a semicolon-separated SGR parameter string,
    /// suitable for embedding in a DECRQSS <c>m</c> response.
    /// </summary>
    /// <remarks>
    /// Every code emitted here is one this handler parses back, so a program can read the reply,
    /// replay it, and land on the attributes it started from — which is the point of asking. What
    /// is off, and a colour that is still the default, is left out; nothing at all reads as
    /// <c>0</c>, the reset.
    /// </remarks>
    private string SerializeSgr()
    {
        var attr = _curAttr;

        // Build a list of SGR code fragments. Each may be a single number ("1") or a
        // semicolon-separated run ("38;2;255;128;0").
        var parts = new List<string>(8);

        if (attr.IsBold()) parts.Add("1");
        if (attr.IsDim()) parts.Add("2");
        if (attr.IsItalic()) parts.Add("3");

        switch (attr.GetUnderlineStyle())
        {
            case UnderlineStyle.Single: parts.Add("4"); break;
            case UnderlineStyle.Double: parts.Add("21"); break;
            case UnderlineStyle.Curly: parts.Add("4:3"); break;
            case UnderlineStyle.Dotted: parts.Add("4:4"); break;
            case UnderlineStyle.Dashed: parts.Add("4:5"); break;
        }

        if (attr.IsBlink()) parts.Add("5");
        if (attr.IsInverse()) parts.Add("7");
        if (attr.IsInvisible()) parts.Add("8");
        if (attr.IsStrikethrough()) parts.Add("9");
        if (attr.IsOverline()) parts.Add("53");

        // Foreground colour
        var fgMode = attr.GetFgColorMode();
        var fg = attr.GetFgColor();
        if (fgMode == 1)
        {
            // RGB truecolor
            parts.Add($"38;2;{(fg >> 16) & 0xFF};{(fg >> 8) & 0xFF};{fg & 0xFF}");
        }
        else if (fg <= 7)
        {
            parts.Add($"{30 + fg}");
        }
        else if (fg <= 15)
        {
            parts.Add($"{90 + fg - 8}");
        }
        else if (fg <= 255)
        {
            parts.Add($"38;5;{fg}");
        }
        // 256 (default fg) → omit

        // Background colour
        var bgMode = attr.GetBgColorMode();
        var bg = attr.GetBgColor();
        if (bgMode == 1)
        {
            // RGB truecolor
            parts.Add($"48;2;{(bg >> 16) & 0xFF};{(bg >> 8) & 0xFF};{bg & 0xFF}");
        }
        else if (bg <= 7)
        {
            parts.Add($"{40 + bg}");
        }
        else if (bg <= 15)
        {
            parts.Add($"{100 + bg - 8}");
        }
        else if (bg <= 255)
        {
            parts.Add($"48;5;{bg}");
        }
        // 257 (default bg) → omit

        return parts.Count == 0 ? "0" : string.Join(";", parts);
    }

    /// <summary>
    /// Serialises the current cursor style as the numeric DECSCUSR parameter.
    /// </summary>
    private string SerializeDecscusr()
    {
        var style = _terminal.Options.CursorStyle;
        var blink = _terminal.Options.CursorBlink;
        return (style, blink) switch
        {
            (CursorStyle.Block, true) => "1",
            (CursorStyle.Block, false) => "2",
            (CursorStyle.Underline, true) => "3",
            (CursorStyle.Underline, false) => "4",
            (CursorStyle.Bar, true) => "5",
            (CursorStyle.Bar, false) => "6",
            _ => "0",
        };
    }

    // CSI Handler Implementations

    private void CursorUp(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        // The row mirror of CursorForward's rule, which this had all along and this did not: a
        // cursor that starts inside the scrolling region stops at its margin, one that starts
        // outside stops at the screen edge. Without it, CSI 10 A from inside a region walked out
        // of the pane and a full-screen editor's status line scrolled with the text.
        _buffer.SetCursor(_buffer.X, Math.Max(_buffer.Y - count, TopLimit()));
    }

    private void CursorDown(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Min(_buffer.Y + count, BottomLimit()));
    }

    /// <summary>
    /// BS. Left one column, and on reverse wraparound (DECSET 45) off the left edge onto the row
    /// above.
    /// </summary>
    /// <remarks>
    /// Here rather than beside the other C0 controls so it reads the same margins every other
    /// cursor motion reads. Bounded at column zero and row zero instead, it walked out of a
    /// DECSLRM pane and, at the top of a DECSTBM region, into the protected row above it.
    /// </remarks>
    internal void Backspace()
    {
        // The left margin, not column zero -- the mirror of where CursorBackward stops.
        var home = CursorInMarginColumns() ? _buffer.ScrollLeft : 0;

        if (_buffer.X > home)
        {
            _buffer.SetCursor(_buffer.X - 1, _buffer.Y);
            return;
        }

        if (!_terminal.ReverseWraparound)
            return;

        // TopLimit, so a cursor that starts inside the scrolling region stays in it. Reverse wrap
        // is what a shell uses to erase a wrapped command line, and that line belongs to the pane
        // it was typed in.
        if (_buffer.Y <= TopLimit())
            return;

        // The right MARGIN, not the screen edge: the row above ends where the pane ends.
        var right = CursorInMarginColumns() ? _buffer.ScrollRight : _terminal.Cols - 1;
        _buffer.SetCursor(right, _buffer.Y - 1);
    }

    private void CursorForward(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        // Stops at the right margin when the cursor starts inside the region, the screen edge
        // when it starts outside — in/out decides, not origin mode, as in xterm. Without the
        // bound, CSI 200 C walks the cursor out of its pane and the next write lands in the
        // neighbouring one. (Full-width margins make the two limits the same column.)
        var limit = CursorInMarginColumns() ? _buffer.ScrollRight : _terminal.Cols - 1;
        _buffer.SetCursor(Math.Min(_buffer.X + count, limit), _buffer.Y);
    }

    private void CursorBackward(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        // The mirror of CursorForward: the left margin stops a cursor that starts inside.
        var home = CursorInMarginColumns() ? _buffer.ScrollLeft : 0;

        // Printing to the end of a line leaves X one PAST the last column with PendingWrap set --
        // a position no character occupies. Counting back from there put the cursor one column
        // right of where every other terminal puts it, so a shell redrawing its line overwrote
        // the wrong character.
        var from = _buffer.PendingWrap ? _buffer.X - 1 : _buffer.X;
        _buffer.SetCursor(Math.Max(from - count, home), _buffer.Y);
    }

    private void CursorNextLine(Params parameters)
    {
        // xterm implements CNL as CUD then CR, so the column is CR’s: the left margin when the
        // cursor is at or right of it, column 0 when it is left of it — origin mode is not
        // consulted. The row move cannot change X, so the CR sees the starting column.
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Min(_buffer.Y + count, BottomLimit()));
        _buffer.CarriageReturn();
    }

    private void CursorPrecedingLine(Params parameters)
    {
        // CPL is CUU then CR, mirroring CursorNextLine.
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Max(_buffer.Y - count, TopLimit()));
        _buffer.CarriageReturn();
    }

    private void CursorCharAbsolute(Params parameters)
    {
        var col = GetAbsoluteCursorCol(Math.Max(parameters.GetParam(0, 1), 1) - 1);
        _buffer.SetCursor(col, _buffer.Y);
    }

    private void CursorPosition(Params parameters)
    {
        var row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        var col = Math.Max(parameters.GetParam(1, 1), 1) - 1;
        row = GetAbsoluteCursorRow(row);
        col = GetAbsoluteCursorCol(col);
        _buffer.SetCursor(col, row);
    }

    private void EraseInDisplay(Params parameters)
    {
        var mode = parameters.GetParam(0, 0);
        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = GetEraseAttributes();

        var hasBlocks = _buffer.HasMultiRowSizedRuns;

        switch (mode)
        {
            case 0: // Erase below
                EraseInLine(parameters); // Current line from cursor
                for (int i = _buffer.Y + 1; i < _terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                    if (hasBlocks)
                        EraseBlocksHangingOver(_buffer.YBase + i, 0, _terminal.Cols);
                }
                break;
            case 1: // Erase above
                for (int i = 0; i < _buffer.Y; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                    if (hasBlocks)
                        EraseBlocksHangingOver(_buffer.YBase + i, 0, _terminal.Cols);
                }
                EraseInLine(parameters); // Current line to cursor
                break;
            case 2: // Erase all — the visible screen only; the scrollback is kept
                for (int i = 0; i < _terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                    if (hasBlocks)
                        EraseBlocksHangingOver(_buffer.YBase + i, 0, _terminal.Cols);
                }
                break;
            case 3: // Erase scrollback (xterm extension) — the scrollback only; the screen is kept
                // Previously shared the body above, which erases the VISIBLE screen and never touches the
                // scrollback: the opposite of what mode 3 asks for. The two modes are complements, not
                // variations, so a caller wanting both sends 2 and 3 — which is exactly what cmd.exe's
                // `cls` does under ConPTY (it clears the screen line by line, then sends CSI 3 J).
                //
                // Discarding rather than blanking is the point: blanked lines are still scrollable, so the
                // history stayed reachable with the mouse wheel even though the terminal had been told to
                // throw it away.
                _buffer.ClearScrollback();
                break;
        }

        // An erase is the likeliest way for the last tall block to leave the buffer, and a flag left
        // set retires the print fast path for the rest of the session.
        if (hasBlocks)
            _buffer.RefreshMultiRowSizedRuns();
    }

    private void EraseInLine(Params parameters)
    {
        var mode = parameters.GetParam(0, 0);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = GetEraseAttributes();

        switch (mode)
        {
            case 0: // Erase to right
                line.Fill(emptyCell, _buffer.X, _terminal.Cols);
                if (_buffer.HasMultiRowSizedRuns)
                    EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, _buffer.X, _terminal.Cols - _buffer.X);
                break;
            case 1: // Erase to left
                line.Fill(emptyCell, 0, _buffer.X + 1);
                if (_buffer.HasMultiRowSizedRuns)
                    EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, 0, _buffer.X + 1);
                break;
            case 2: // Erase entire line
                line.Fill(emptyCell);
                if (_buffer.HasMultiRowSizedRuns)
                    EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, 0, _terminal.Cols);
                break;
        }
    }

    /// <summary>
    /// Erases the OSC 66 blocks anchored on earlier rows whose cells hang over the region an erase
    /// or a line splice is about to change.
    /// </summary>
    /// <remarks>
    /// The protocol's rules for erasing and for inserting or deleting lines are stated over the
    /// REGION affected rather than over a line, and a block <c>s</c> rows tall is inside every region
    /// its lower rows touch. A block left alive over rows that have been cleared or moved would be
    /// drawn across whatever is there now, and its columns would go on displacing text written to
    /// rows that are no longer under it.
    /// </remarks>
    /// <summary>
    /// Erases every block covering the given cells from an earlier row. Callers test
    /// <see cref="TerminalBuffer.HasMultiRowSizedRuns"/> AT THE CALL — hoisted outside the loop
    /// where there is one — for the reason NoteLinkRun's guard records: these sit on the
    /// erase hot paths, a full-screen redraw erases every line, and a method call per line just
    /// to read a false flag is the shape that has now cost alt-redraw twice.
    /// </summary>
    private void EraseBlocksHangingOver(int absoluteRow, int column, int count) =>
        _buffer.EraseSizedRunsCovering(absoluteRow, column, count);

    /// <summary>
    /// Whether the cursor is inside the margin columns — the ONE in/out answer every
    /// column-sensitive operation shares, so no two of them can disagree about the same column.
    /// </summary>
    /// <remarks>
    /// The boundary column is the subtle part. X == ScrollRight + 1 is two different states: the
    /// pending-wrap residue of filling the region's last column (INSIDE — the wrap is due at the
    /// margin), and a deliberate placement at the first column right of the margin (OUTSIDE — an
    /// ordinary cursor position in the split layouts this feature exists for). The buffer's
    /// <see cref="TerminalBuffer.PendingWrap"/> flag is what tells them apart; deciding by
    /// position alone either wrapped the next pane's first column into this one, or ran text
    /// straight through the margin at exactly the moment the wrap was due.
    /// </remarks>
    private bool CursorInMarginColumns()
        => _buffer.X >= _buffer.ScrollLeft
        && _buffer.X <= _buffer.ScrollRight + (_buffer.PendingWrap ? 1 : 0);

    /// <summary>
    /// Whether the cursor is inside the scrolling region — the box, not just the band of rows.
    /// </summary>
    /// <remarks>
    /// IL and DL do nothing from outside it. With margins that has to include the columns: a cursor
    /// in the right-hand pane of a split layout is outside the left pane's region, and shifting the
    /// left pane's lines from there is the exact corruption margins exist to prevent. The column
    /// test goes through <see cref="CursorInMarginColumns"/> so the pending-wrap state counts as
    /// inside — with no margins at all, a cursor resting at X == Cols after a full-width line is
    /// the ORDINARY place for IL/DL to run from, and reading it as outside made them no-ops on the
    /// default path.
    /// </remarks>
    private bool InsideScrollRegion()
        => _buffer.Y >= _buffer.ScrollTop && _buffer.Y <= _buffer.ScrollBottom
        && CursorInMarginColumns();

    private void InsertLines(Params parameters)
    {
        // Clamped to what the operation can actually reach: from the cursor's row to the bottom of
        // the region. Clamping to the screen height still allowed iterations over rows outside the
        // region, which the splice loop below cannot touch.
        var count = Math.Min(Math.Max(parameters.GetParam(0, 1), 1),
                             Math.Max(_buffer.ScrollBottom - _buffer.Y + 1, 1));
        if (!InsideScrollRegion())
            return;

        // Both move the cursor to the left margin, which every reference terminal does and none
        // of the paths below did: an editor inserting a line then writing to it started from
        // wherever the cursor happened to sit.
        _buffer.SetCursor(CursorInMarginColumns() ? _buffer.ScrollLeft : 0, _buffer.Y);

        // Narrowed margins move only their own columns, so the lines stay put and their cells are
        // copied between them. Splicing whole lines here would drag the columns OUTSIDE the region
        // along with them, which is the side-by-side layout tearing itself apart. That path erases
        // the blocks it disturbs itself, since it knows which columns it moved.
        if (!_buffer.MarginsAreFullWidth)
        {
            _buffer.ScrollMarginColumns(_buffer.Y, _buffer.ScrollBottom, count, up: false, BlankCell());
            return;
        }

        // A block hanging over the cursor's row is split by the insertion -- its lower rows are
        // pushed away from the line that describes them -- so the protocol has it erased.
        if (_buffer.HasMultiRowSizedRuns)
            EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, 0, _terminal.Cols);

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.YBase + _buffer.ScrollBottom, 1);
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 0,
                _buffer.GetBlankLine(GetEraseAttributes()));
        }

        _buffer.RefreshMultiRowSizedRuns();
    }

    private void DeleteLines(Params parameters)
    {
        // Clamped to what the operation can actually reach: from the cursor's row to the bottom of
        // the region. Clamping to the screen height still allowed iterations over rows outside the
        // region, which the splice loop below cannot touch.
        var count = Math.Min(Math.Max(parameters.GetParam(0, 1), 1),
                             Math.Max(_buffer.ScrollBottom - _buffer.Y + 1, 1));
        if (!InsideScrollRegion())
            return;

        // Both move the cursor to the left margin, which every reference terminal does and none
        // of the paths below did: an editor inserting a line then writing to it started from
        // wherever the cursor happened to sit.
        _buffer.SetCursor(CursorInMarginColumns() ? _buffer.ScrollLeft : 0, _buffer.Y);

        if (!_buffer.MarginsAreFullWidth)
        {
            _buffer.ScrollMarginColumns(_buffer.Y, _buffer.ScrollBottom, count, up: true, BlankCell());
            return;
        }

        // Every deleted row is part of the region, so a block hanging over any of them goes too.
        // The blocks anchored ON those rows leave with the lines that describe them.
        var last = Math.Min(_buffer.Y + count - 1, _buffer.ScrollBottom);
        for (int row = _buffer.Y; row <= last; row++)
            if (_buffer.HasMultiRowSizedRuns)
                EraseBlocksHangingOver(row + _buffer.YBase, 0, _terminal.Cols);

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 1);
            _buffer.Lines.Splice(_buffer.YBase + _buffer.ScrollBottom, 0,
                _buffer.GetBlankLine(GetEraseAttributes()));
        }

        _buffer.RefreshMultiRowSizedRuns();
    }

    private void InsertChars(Params parameters)
    {
        SettleForEditing();
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        // The MARGINS bound this, not the screen — both of them. Shifting past the right margin
        // would push characters out of one pane and into the next; running from a cursor LEFT of
        // the left margin shifts the neighbouring pane's columns across it from outside, the same
        // corruption from the other side. Outside the region on either side, ICH does nothing.
        var right = _buffer.ScrollRight;
        if (_buffer.X > right || _buffer.X < _buffer.ScrollLeft)
            return;

        // A scaled block from the cursor rightwards cannot survive the shift: the cells that make it
        // up move, and the run describing them does not. The protocol says to erase them.
        if (line.HasSizedRuns)
            line.EraseSizedRunsFrom(_buffer.X);

        count = Math.Min(count, right - _buffer.X + 1);

        line.CopyCellsFrom(line, _buffer.X, _buffer.X + count,
            right - _buffer.X - count + 1, false);

        line.Fill(BlankCell(), _buffer.X, Math.Min(_buffer.X + count, right + 1));
    }

    private void DeleteChars(Params parameters)
    {
        SettleForEditing();
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        // As with ICH, the margins are the edges — what is pulled in comes from inside the
        // region, the blanks appear at the margin rather than the screen edge, and a cursor
        // outside the region on EITHER side does nothing rather than dragging the next pane's
        // columns across the boundary.
        var right = _buffer.ScrollRight;
        if (_buffer.X > right || _buffer.X < _buffer.ScrollLeft)
            return;

        count = Math.Min(count, right - _buffer.X + 1);

        // As for insertion: shifting cells destroys any block from the cursor rightwards.
        if (line.HasSizedRuns)
            line.EraseSizedRunsFrom(_buffer.X);

        line.CopyCellsFrom(line, _buffer.X + count, _buffer.X,
            right - _buffer.X - count + 1, false);

        line.Fill(BlankCell(), right - count + 1, right + 1);
    }

    private void EraseChars(Params parameters)
    {
        SettleForEditing();
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];

        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = GetEraseAttributes();

        line?.Fill(emptyCell, _buffer.X, Math.Min(_buffer.X + count, _terminal.Cols));
        if (_buffer.HasMultiRowSizedRuns)
            EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, _buffer.X,
            Math.Min(_buffer.X + count, _terminal.Cols) - _buffer.X);
    }

    private void ScrollUp(Params parameters)
    {
        // Clamped to the screen: scrolling by more than the region holds leaves it blank either
        // way, and the buffer scrolls a line at a time -- CSI 2000000000 S spent minutes arriving
        // at the same picture as CSI 67 S.
        var count = Math.Min(Math.Max(parameters.GetParam(0, 1), 1), _terminal.Rows);
        _buffer.ScrollUp(count);
    }

    private void ScrollDown(Params parameters)
    {
        // Clamped to the screen: scrolling by more than the region holds leaves it blank either
        // way, and the buffer scrolls a line at a time -- CSI 2000000000 S spent minutes arriving
        // at the same picture as CSI 67 S.
        var count = Math.Min(Math.Max(parameters.GetParam(0, 1), 1), _terminal.Rows);
        _buffer.ScrollDown(count);
    }

    private void SaveCursorAnsi()
    {
        // ANSI save cursor (CSI s) - same as DEC DECSC but simpler
        SaveCursor();
    }

    private void RestoreCursorAnsi()
    {
        // ANSI restore cursor (CSI u) - same as DEC DECRC but simpler
        RestoreCursor();
    }

    private void LinePositionAbsolute(Params parameters)
    {
        // VPA - Line Position Absolute (CSI d)
        var row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        row = GetAbsoluteCursorRow(row);
        _buffer.SetCursor(_buffer.X, row);
    }

    private void CursorForwardTab(Params parameters)
    {
        // CHT - Cursor Forward Tabulation (CSI I)
        var count = Math.Max(parameters.GetParam(0, 1), 1);

        for (var i = 0; i < count; i++)
            _buffer.SetCursor(_terminal.NextTabStop(_buffer.X), _buffer.Y);
    }

    private void CursorBackwardTab(Params parameters)
    {
        // CBT - Cursor Backward Tabulation (CSI Z)
        var count = Math.Max(parameters.GetParam(0, 1), 1);

        for (var i = 0; i < count; i++)
        {
            if (_buffer.X == 0)
                break;

            // The stop SET, like HT and CHT. Deriving the previous stop arithmetically ignored
            // every stop a program set with HTS and every one it cleared with TBC, so backward
            // tab disagreed with forward tab on the same screen.
            _buffer.SetCursor(_terminal.PreviousTabStop(_buffer.X), _buffer.Y);
        }
    }

    private void TabClear(Params parameters)
    {
        // TBC - Tab Clear (CSI g). Ps=0 clears the stop at the cursor, Ps=3 clears them all.
        // This used to acknowledge both and do nothing, which its own comment admitted, so
        // `tabs 4` and every program that manages its own stops was quietly ignored.
        var mode = parameters.GetParam(0, 0);
        if (mode == 0)
            _terminal.ClearTabStop(_buffer.X, all: false);
        else if (mode == 3)
            _terminal.ClearTabStop(0, all: true);
    }

    /// <summary>
    /// XTSMGRAPHICS -- CSI ? Pi ; Pa ; Pv S. Reports the terminal's graphics limits.
    /// </summary>
    /// <remarks>
    /// <para>This shares its final character with SCROLL UP, and <c>ToCsiCommand</c> used to strip
    /// the private marker before looking the command up, so a graphics query scrolled the screen
    /// instead of being answered. Every Sixel-capable program sends one during startup, which made
    /// the damage routine rather than obscure. The lookup now matches "?S" on its own.</para>
    /// <para>Only the read operations are answered. The limits are fixed, so accepting a request
    /// to change them and quietly not doing it would be worse than refusing outright.</para>
    /// </remarks>
    private void GraphicsAttributes(Params parameters)
    {
        const int readAttribute = 1;
        const int readDefault = 2;
        const int readMaximum = 4;

        const int success = 0;
        const int badItem = 1;
        const int badAction = 2;

        var item = parameters.GetParam(0, 0);
        var action = parameters.GetParam(1, 0);
        var isRead = action == readAttribute || action == readDefault || action == readMaximum;

        switch (item)
        {
            case 1: // number of colour registers
                _terminal.RaiseDataReceived(isRead
                    ? $"\u001b[?1;{success};{Graphics.SixelPalette.RegisterCount}S"
                    : $"\u001b[?1;{badAction}S");
                break;

            case 2: // Sixel geometry
                if (isRead)
                {
                    // Reported as what MaxSixelPixels allows across the full terminal width, so a
                    // program that sizes an image to fit gets one we will not then throw away.
                    var width = Math.Max(1, _terminal.Cols * Math.Max(1, _terminal.Options.CellWidthPixels));
                    var height = Math.Max(1, _terminal.Options.MaxSixelPixels / width);
                    _terminal.RaiseDataReceived($"\u001b[?2;{success};{width};{height}S");
                }
                else
                {
                    _terminal.RaiseDataReceived($"\u001b[?2;{badAction}S");
                }
                break;

            default:
                _terminal.RaiseDataReceived($"\u001b[?{item};{badItem}S");
                break;
        }
    }

    private void DeviceStatusReport(Params parameters, bool isPrivate)
    {
        // DSR - Device Status Report (CSI n or CSI ? n)
        var report = parameters.GetParam(0, 0);

        if (isPrivate)
        {
            // DEC-specific DSR
            switch (report)
            {
                case 6: // DECXCPR - Extended Cursor Position Report
                    // Report cursor position: CSI ? row ; col R
                    var row = _buffer.Y + 1; // 1-based
                    var col = _buffer.X + 1; // 1-based
                    _terminal.RaiseDataReceived($"\u001b[?{row};{col}R");
                    break;

                case 15: // Printer status
                    // Report no printer: CSI ? 1 3 n
                    _terminal.RaiseDataReceived("\u001b[?13n");
                    break;

                case 25: // UDK status
                    // Report UDK locked: CSI ? 2 1 n
                    _terminal.RaiseDataReceived("\u001b[?21n");
                    break;

                case 26: // Keyboard status
                    // Report keyboard ready: CSI ? 2 7 ; 1 ; 0 ; 0 n
                    _terminal.RaiseDataReceived("\u001b[?27;1;0;0n");
                    break;
            }
        }
        else
        {
            // Standard ANSI DSR
            switch (report)
            {
                case 5: // Operating status
                    // Report OK: CSI 0 n
                    _terminal.RaiseDataReceived("\u001b[0n");
                    break;

                case 6: // CPR - Cursor Position Report
                    // Report cursor position: CSI row ; col R
                    var row = _buffer.Y + 1; // 1-based
                    var col = _buffer.X + 1; // 1-based

                    // Adjust for origin mode
                    if (_terminal.OriginMode)
                    {
                        row = row - _buffer.ScrollTop;
                    }

                    _terminal.RaiseDataReceived($"\u001b[{row};{col}R");
                    break;
            }
        }
    }

    private void CharAttributes(Params parameters)
    {
        if (parameters.Length == 0)
        {
            _curAttr = AttributeData.Default;
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters.GetParam(i, 0);

            switch (param)
            {
                case 0: // Reset
                    _curAttr = AttributeData.Default;
                    break;
                case 1: // Bold
                    _curAttr.SetBold(true);
                    break;
                case 2: // Dim
                    _curAttr.SetDim(true);
                    break;
                case 3: // Italic
                    _curAttr.SetItalic(true);
                    break;
                case 21: // Double underline
                    _curAttr.SetUnderlineStyle(UnderlineStyle.Double);
                    break;
                case 4: // Underline, with an optional style as a sub-parameter
                    _curAttr.SetUnderlineStyle(ReadUnderlineStyle(parameters, i));
                    break;
                case 5: // Blink
                    _curAttr.SetBlink(true);
                    break;
                case 7: // Inverse
                    _curAttr.SetInverse(true);
                    break;
                case 8: // Invisible
                    _curAttr.SetInvisible(true);
                    break;
                case 9: // Strikethrough
                    _curAttr.SetStrikethrough(true);
                    break;
                case 22: // Not bold/dim
                    _curAttr.SetBold(false);
                    _curAttr.SetDim(false);
                    break;
                case 23: // Not italic
                    _curAttr.SetItalic(false);
                    break;
                case 24: // Not underline
                    _curAttr.SetUnderline(false);
                    break;
                case 25: // Not blink
                    _curAttr.SetBlink(false);
                    break;
                case 27: // Not inverse
                    _curAttr.SetInverse(false);
                    break;
                case 28: // Not invisible
                    _curAttr.SetInvisible(false);
                    break;
                case 29: // Not strikethrough
                    _curAttr.SetStrikethrough(false);
                    break;
                case >= 30 and <= 37: // Foreground color
                    _curAttr.SetFgColor(param - 30);
                    break;
                case 58: // Underline colour
                    i = HandleUnderlineColor(parameters, i);
                    break;
                case 59: // Underline colour back to the foreground
                    _curAttr.ResetUnderlineColor();
                    break;
                case 38: // Extended foreground color
                    i = HandleExtendedColor(parameters, i, true);
                    break;
                case 39: // Default foreground
                    _curAttr.SetFgColor(256);
                    break;
                case >= 40 and <= 47: // Background color
                    _curAttr.SetBgColor(param - 40);
                    break;
                case 48: // Extended background color
                    i = HandleExtendedColor(parameters, i, false);
                    break;
                case 49: // Default background
                    _curAttr.SetBgColor(257);
                    break;
                case >= 90 and <= 97: // Bright foreground color
                    _curAttr.SetFgColor(param - 90 + 8);
                    break;
                case >= 100 and <= 107: // Bright background color
                    _curAttr.SetBgColor(param - 100 + 8);
                    break;
            }
        }
    }

    /// <summary>
    /// DECSLRM (<c>CSI Pl ; Pr s</c>) — set the left and right margins of the scrolling region.
    /// </summary>
    /// <remarks>
    /// <para>Only reachable while DECLRMM is set; see the dispatch above for why.</para>
    /// <para>Omitted parameters mean the extremes, so a bare <c>CSI s</c> under the mode widens the
    /// margins back to the whole screen rather than doing nothing.</para>
    /// <para>The cursor goes home afterwards, as it does for DECSTBM. A cursor left outside the new
    /// region is the thing that makes the next write land somewhere the application did not choose.</para>
    /// </remarks>
    private void SetLeftRightMargins(Params parameters)
    {
        var left = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        var right = Math.Max(parameters.GetParam(1, _terminal.Cols), 1) - 1;

        // A degenerate pair is refused outright, and then so is the cursor move: DEC leaves the old
        // margins in force, and homing the cursor to a region that was not set would be a visible
        // effect from a sequence that had none.
        if (!_buffer.SetLeftRightMargins(left, right))
            return;

        MoveCursorToHome();
    }

    private void SetScrollRegion(Params parameters)
    {
        // An explicit 0 means the default, exactly as a missing parameter does -- GetParam only
        // substitutes for ABSENT values, and the parser seeds parameters with 0, so both CSI 0;0r
        // and the bare CSI ;r arrived as zeros and clamped the region to a single row. A shell
        // resetting its scroll region with CSI 0;0r ended up with a one-row screen.
        var topParam = parameters.GetParam(0, 1);
        var bottomParam = parameters.GetParam(1, _terminal.Rows);
        var top = Math.Max(topParam <= 0 ? 1 : topParam, 1) - 1;
        var bottom = Math.Max(bottomParam <= 0 ? _terminal.Rows : bottomParam, 1) - 1;
        _buffer.SetScrollRegion(top, bottom);
        MoveCursorToHome();
    }

    private int GetAbsoluteCursorRow(int row)
    {
        if (_terminal.OriginMode)
        {
            long absoluteRow = (long)_buffer.ScrollTop + row;
            return (int)Math.Clamp(absoluteRow, _buffer.ScrollTop, _buffer.ScrollBottom);
        }

        return Math.Clamp(row, 0, _terminal.Rows - 1);
    }

    private void MoveCursorToHome()
    {
        // Home is the top-left of the SCROLLING REGION under origin mode, which with margins is a
        // box rather than a band -- so the column matters as well as the row.
        var row = _terminal.OriginMode ? _buffer.ScrollTop : 0;
        var col = _terminal.OriginMode ? _buffer.ScrollLeft : 0;
        _buffer.SetCursor(col, row);
    }

    /// <summary>
    /// Turns a column an application asked for into an absolute one, honouring origin mode.
    /// </summary>
    /// <remarks>
    /// The column twin of <see cref="GetAbsoluteCursorRow"/>. Under origin mode an application
    /// addresses the region rather than the screen, so column 1 is the left margin and nothing it
    /// asks for can land outside the box.
    /// </remarks>
    private int GetAbsoluteCursorCol(int col)
    {
        if (_terminal.OriginMode)
        {
            long absolute = (long)_buffer.ScrollLeft + col;
            return (int)Math.Clamp(absolute, _buffer.ScrollLeft, _buffer.ScrollRight);
        }

        return Math.Clamp(col, 0, Math.Max(0, _terminal.Cols - 1));
    }

    private void WindowManipulation(Params parameters)
    {
        // CSI Ps ; Ps ; Ps t - Window manipulation (XTWINOPS)
        // Check WindowOptions permissions before firing events
        var operation = parameters.GetParam(0, 0);

        switch (operation)
        {
            case 1: // De-iconify window (restore from minimized)
                if (_terminal.Options.WindowOptions.RestoreWin)
                {
                    _terminal.RaiseWindowRestored();
                }
                break;

            case 2: // Iconify window (minimize)
                if (_terminal.Options.WindowOptions.MinimizeWin)
                {
                    _terminal.RaiseWindowMinimized();
                }
                break;

            case 3: // Move window to x, y
                if (_terminal.Options.WindowOptions.SetWinPosition)
                {
                    var x = parameters.GetParam(1, 0);
                    var y = parameters.GetParam(2, 0);
                    _terminal.RaiseWindowMoved(x, y);
                }
                break;

            case 4: // Resize window to height, width pixels
                if (_terminal.Options.WindowOptions.SetWinSizePixels)
                {
                    var height = parameters.GetParam(1, 0);
                    var width = parameters.GetParam(2, 0);
                    _terminal.RaiseWindowResized(width, height);
                }
                break;

            case 5: // Raise window to front
                if (_terminal.Options.WindowOptions.RaiseWin)
                {
                    _terminal.RaiseWindowRaised();
                }
                break;

            case 6: // Lower window to back
                if (_terminal.Options.WindowOptions.LowerWin)
                {
                    _terminal.RaiseWindowLowered();
                }
                break;

            case 7: // Refresh window
                if (_terminal.Options.WindowOptions.RefreshWin)
                {
                    _terminal.RaiseWindowRefreshed();
                }
                break;

            case 8: // Resize text area to height, width characters
                if (_terminal.Options.WindowOptions.SetWinSizeChars)
                {
                    var rows = parameters.GetParam(1, 0);
                    var cols = parameters.GetParam(2, 0);
                    if (rows > 0 && cols > 0)
                    {
                        _terminal.Resize(cols, rows);
                    }
                }
                break;

            case 9: // Maximize/restore operations
                var subOp = parameters.GetParam(1, 0);
                if (subOp == 0 && _terminal.Options.WindowOptions.RestoreWin)
                {
                    // Restore maximized window
                    _terminal.RaiseWindowRestored();
                }
                else if (subOp == 1 && _terminal.Options.WindowOptions.MaximizeWin)
                {
                    // Maximize window
                    _terminal.RaiseWindowMaximized();
                }
                break;

            case 10: // Full-screen operations
                subOp = parameters.GetParam(1, 0);
                if (subOp == 0 && _terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Exit full-screen
                    _terminal.RaiseWindowFullscreened();
                }
                else if (subOp == 1 && _terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Enter full-screen
                    _terminal.RaiseWindowFullscreened();
                }
                else if (subOp == 2 && _terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Toggle full-screen
                    _terminal.RaiseWindowFullscreened();
                }
                break;

            case 11: // Report window state (iconified or not)
                if (_terminal.Options.WindowOptions.GetWinState)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.State);
                    if (args.Handled)
                    {
                        // Response: CSI 1 t (not iconified) or CSI 2 t (iconified)
                        var stateCode = args.IsIconified ? 2 : 1;
                        _terminal.RaiseDataReceived($"\u001b[{stateCode}t");
                    }
                }
                break;

            case 13: // Report window position
                if (_terminal.Options.WindowOptions.GetWinPosition)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.Position);
                    if (args.Handled)
                    {
                        // Response: CSI 3 ; x ; y t
                        _terminal.RaiseDataReceived($"\u001b[3;{args.X};{args.Y}t");
                    }
                }
                break;

            case 14: // Report window size in pixels
                if (_terminal.Options.WindowOptions.GetWinSizePixels)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.SizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 4 ; height ; width t
                        _terminal.RaiseDataReceived($"\u001b[4;{args.HeightPixels};{args.WidthPixels}t");
                    }
                }
                break;

            case 15: // Report screen size in pixels
                if (_terminal.Options.WindowOptions.GetScreenSizePixels)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.ScreenSizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 5 ; height ; width t
                        _terminal.RaiseDataReceived($"\u001b[5;{args.HeightPixels};{args.WidthPixels}t");
                    }
                }
                break;

            case 16: // Report character cell size in pixels
                if (_terminal.Options.WindowOptions.GetCellSizePixels)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.CellSizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 6 ; height ; width t
                        _terminal.RaiseDataReceived($"\u001b[6;{args.CellHeight};{args.CellWidth}t");
                    }
                }
                break;

            case 18: // Report text area size in characters
                if (_terminal.Options.WindowOptions.GetWinSizeChars)
                {
                    // Response: CSI 8 ; rows ; cols t
                    _terminal.RaiseDataReceived($"\u001b[8;{_terminal.Rows};{_terminal.Cols}t");
                }
                break;

            case 19: // Report screen size in characters
                if (_terminal.Options.WindowOptions.GetScreenSizePixels)
                {
                    // This is typically the same as window size for terminal apps
                    _terminal.RaiseDataReceived($"\u001b[9;{_terminal.Rows};{_terminal.Cols}t");
                }
                break;

            case 20: // Report icon label
                if (_terminal.Options.WindowOptions.GetIconTitle)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.IconTitle);
                    if (args.Handled && args.Title != null)
                    {
                        // Response: OSC L label ST
                        _terminal.RaiseDataReceived($"\u001b]L{args.Title}\u0007");
                    }
                }
                break;

            case 21: // Report window title
                if (_terminal.Options.WindowOptions.GetWinTitle)
                {
                    // Response: OSC l title ST - use the terminal's current title
                    var title = _terminal.Title ?? string.Empty;
                    _terminal.RaiseDataReceived($"\u001b]l{title}\u0007");
                }
                break;

            case 22: // Save window title
                // Push title onto stack (not typically implemented)
                break;

            case 23: // Restore window title
                // Pop title from stack (not typically implemented)
                break;
        }
    }

    // ESC Handler Implementations

    private void IndexDown()
    {
        if (_buffer.Y == _buffer.ScrollBottom)
        {
            _buffer.ScrollUp(1);
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y + 1);
        }
    }

    private void NextLine()
    {
        // NEL is Index plus carriage return in xterm, so the column follows CR’s margin rule.
        IndexDown();
        _buffer.CarriageReturn();
    }

    private void ReverseIndex()
    {
        if (_buffer.Y == _buffer.ScrollTop)
        {
            _buffer.ScrollDown(1);
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y - 1);
        }
    }

    private void SelectCursorStyle(Params parameters)
    {
        // DECSCUSR - Select Cursor Style (CSI Ps SP q)
        var ps = parameters.GetParam(0, 1);

        CursorStyle style;
        bool blink;

        switch (ps)
        {
            case 0:
            case 1:
                style = CursorStyle.Block;
                blink = true;
                break;
            case 2:
                style = CursorStyle.Block;
                blink = false;
                break;
            case 3:
                style = CursorStyle.Underline;
                blink = true;
                break;
            case 4:
                style = CursorStyle.Underline;
                blink = false;
                break;
            case 5:
                style = CursorStyle.Bar;
                blink = true;
                break;
            case 6:
                style = CursorStyle.Bar;
                blink = false;
                break;
            default:
                // Unsupported value - ignore
                return;
        }

        _terminal.SetCursorStyle(style, blink);
    }

    /// <summary>
    /// DECSC. The cursor's whole context, not just where it is: the charset a program selected,
    /// origin mode, and the pending-wrap flag travel with the position because DECRC is supposed
    /// to put the terminal back the way it was.
    /// </summary>
    /// <remarks>
    /// SavedCursor.Charset had existed unassigned, which is the tell. A program doing
    /// ESC ( 0, DECSC, ESC ( B, DECRC expects line-drawing back and got ASCII, so a TUI that saves
    /// the cursor mid-border finished the box in letters.
    /// </remarks>
    private void SaveCursor()
    {
        _buffer.SavedCursorState.X = _buffer.X;
        _buffer.SavedCursorState.Y = _buffer.Y;
        _buffer.SavedCursorState.Attr = _curAttr;
        _buffer.SavedCursorState.Charset = _currentCharset;
        // The DESIGNATIONS as well as which G-set is active. ESC ( 0 changes what G0 means, not
        // which set is selected, so saving _currentCharset alone restored a pointer to a table
        // the program had since replaced: a TUI that saved the cursor mid-border finished the box
        // in letters.
        var designations = _buffer.SavedCursorState.Designations ??= new Dictionary<char, string>?[4];
        for (var slot = 0; slot < designations.Length; slot++)
            designations[slot] = _charsets.GetValueOrDefault((CharsetMode)slot);
        _buffer.SavedCursorState.OriginMode = _terminal.OriginMode;
        _buffer.SavedCursorState.PendingWrap = _buffer.PendingWrap;
    }

    private void RestoreCursor()
    {
        // SetCursorRaw when the saved cursor was pending a wrap, because that position is X ==
        // Cols -- one past the last column, where no character sits -- and SetCursor clamps it to
        // Cols - 1. Restoring through the clamp put the cursor ON the last cell instead of past
        // it, so the next character overwrote that cell rather than wrapping, and the flag set
        // below could not undo it: the coordinate was already gone.
        if (_buffer.SavedCursorState.PendingWrap)
            _buffer.SetCursorRaw(_buffer.SavedCursorState.X, _buffer.SavedCursorState.Y);
        else
            _buffer.SetCursor(_buffer.SavedCursorState.X, _buffer.SavedCursorState.Y);
        _curAttr = _buffer.SavedCursorState.Attr;
        var designations = _buffer.SavedCursorState.Designations;
        if (designations is not null)
        {
            for (var slot = 0; slot < designations.Length; slot++)
                _charsets[(CharsetMode)slot] = designations[slot];
        }

        _currentCharset = _buffer.SavedCursorState.Charset;
        RefreshActiveCharset();
        _terminal.OriginMode = _buffer.SavedCursorState.OriginMode;
        _buffer.SetPendingWrap(_buffer.SavedCursorState.PendingWrap);
    }

    /// <summary>Restores the rendition state consumed by printing and background erasure.</summary>
    internal void ResetAttributes() => _curAttr = AttributeData.Default;
}
