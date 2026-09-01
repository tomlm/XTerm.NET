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
        // DECALN resets the margins -- top/bottom and left/right alike -- and origin mode, before
        // homing the cursor. The alignment pattern exists for checking screen geometry, and it
        // starts that geometry from scratch; a region surviving it would clip the very pattern.
        _buffer.SetScrollRegion(0, _terminal.Rows - 1);
        _buffer.SetLeftRightMargins(0, _terminal.Cols - 1);
        _terminal.OriginMode = false;

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

        // Led by the reset, as xterm leads it: the reply is meant to be REPLAYED, and without
        // the 0 it composes onto whatever attributes are in force instead of reproducing these.
        parts.Add("0");

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

        // A cursor PENDING a wrap sits one past the last column, a position no character
        // occupies. With reverse wrap on, backspace is absorbed by un-pending: the cursor lands
        // ON the last column and reports the same position it did before -- which is exactly the
        // "backspace has no effect" xterm shows in that state. Without it, the phantom column
        // never existed and backspace acts from the last REAL column.
        if (_buffer.PendingWrap)
        {
            var lastCol = (CursorInMarginColumns() ? _buffer.ScrollRight : _terminal.Cols - 1);
            var absorb = _terminal.Options.Wraparound
                         && (_terminal.ReverseWraparound || _terminal.ReverseWraparoundExtended);
            _buffer.SetCursor(absorb ? lastCol : Math.Max(lastCol - 1, home), _buffer.Y);
            return;
        }

        if (_buffer.X > home)
        {
            _buffer.SetCursor(_buffer.X - 1, _buffer.Y);
            return;
        }

        // Reverse wrap needs DECAWM as well as its own mode -- both flavours of it. xterm split
        // the feature in 2023: mode 45 is INLINE, crossing onto the row above only where the line
        // actually wrapped (which is what erasing a wrapped command line needs and all it needs);
        // mode 1045 is the CLASSIC behaviour, wrapping from any position and, at the top of the
        // region, around to its bottom.
        var classic = _terminal.ReverseWraparoundExtended;
        var inline = _terminal.ReverseWraparound;
        if (!_terminal.Options.Wraparound || (!inline && !classic))
            return;

        // Inline: only a wrap CONTINUATION has a previous row that is part of the same line. A
        // cursor parked at a left margin by addressing has nothing above it to erase.
        if (!classic)
        {
            var line = _buffer.Lines[_buffer.YBase + _buffer.Y];
            if (line is null || !line.IsWrapped)
                return;
        }

        // The right MARGIN when margins are set, the screen edge otherwise -- and the margin even
        // for a cursor left of the left margin: backing off the left EDGE lands on the pane's own
        // last column, which is where xterm has put it since margins arrived in 2012.
        var right = _terminal.LeftRightMarginMode ? _buffer.ScrollRight : _terminal.Cols - 1;

        if (_buffer.Y > TopLimit())
        {
            _buffer.SetCursor(right, _buffer.Y - 1);
            return;
        }

        // At the top of the region, classic reverse wrap carries on around to the region's
        // bottom -- the treatment xterm gave top/bottom margins in 2018 for consistency with the
        // left/right pair. Inline never gets here: the top row of a region cannot be a wrap
        // continuation of the row outside it.
        if (classic && _buffer.Y == TopLimit())
            _buffer.SetCursor(right, BottomLimit());
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

        // With reverse wrap in force, CUB keeps counting across the wrap: a shell backing up
        // over a command line that spilled onto the next row walks the cursor back onto the row
        // above, exactly as that many backspaces would. Each row consumed takes one count for
        // the wrap itself, and inline mode (45) only crosses where the line really wrapped.
        var reverse = _terminal.Options.Wraparound
                      && (_terminal.ReverseWraparound || _terminal.ReverseWraparoundExtended);
        var x = from;
        var y = _buffer.Y;
        while (true)
        {
            var step = Math.Min(count, x - home);
            x -= step;
            count -= step;
            if (count == 0 || !reverse || y <= TopLimit())
                break;

            if (!_terminal.ReverseWraparoundExtended)
            {
                var line = _buffer.Lines[_buffer.YBase + y];
                if (line is null || !line.IsWrapped)
                    break;
            }

            x = CursorInMarginColumns() ? _buffer.ScrollRight : _terminal.Cols - 1;
            y -= 1;
            count -= 1;
            if (count == 0)
                break;
        }

        _buffer.SetCursor(x, y);
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

    private void EraseInDisplay(Params parameters, bool selective = false)
    {
        var mode = parameters.GetParam(0, 0);

        var hasBlocks = _buffer.HasMultiRowSizedRuns;

        switch (mode)
        {
            case 0: // Erase below
                EraseInLine(parameters, selective); // Current line from cursor
                for (int i = _buffer.Y + 1; i < _terminal.Rows; i++)
                {
                    EraseLineCells(_buffer.Lines[_buffer.YBase + i], 0, _terminal.Cols, selective);
                    BreakWrapFromAbove(i);
                    if (hasBlocks)
                        EraseBlocksHangingOver(_buffer.YBase + i, 0, _terminal.Cols);
                }
                break;
            case 1: // Erase above
                for (int i = 0; i < _buffer.Y; i++)
                {
                    EraseLineCells(_buffer.Lines[_buffer.YBase + i], 0, _terminal.Cols, selective);
                    BreakWrapFromAbove(i);
                    if (hasBlocks)
                        EraseBlocksHangingOver(_buffer.YBase + i, 0, _terminal.Cols);
                }
                BreakWrapFromAbove(_buffer.Y);
                EraseInLine(parameters, selective); // Current line to cursor
                break;
            case 2: // Erase all — the visible screen only; the scrollback is kept
                for (int i = 0; i < _terminal.Rows; i++)
                {
                    EraseLineCells(_buffer.Lines[_buffer.YBase + i], 0, _terminal.Cols, selective);
                    BreakWrapFromAbove(i);
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

    private void EraseInLine(Params parameters, bool selective = false)
    {
        var mode = parameters.GetParam(0, 0);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        switch (mode)
        {
            case 0: // Erase to right
                EraseLineCells(line, _buffer.X, _terminal.Cols, selective);
                // With its tail erased, this line no longer wraps onto the next -- xterm's
                // ClearRight clears the wrap flag, and reverse-wrap reads it.
                BreakWrapFromAbove(_buffer.Y + 1);
                if (_buffer.HasMultiRowSizedRuns)
                    EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, _buffer.X, _terminal.Cols - _buffer.X);
                break;
            case 1: // Erase to left
                EraseLineCells(line, 0, _buffer.X + 1, selective);
                if (_buffer.HasMultiRowSizedRuns)
                    EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, 0, _buffer.X + 1);
                break;
            case 2: // Erase entire line
                EraseLineCells(line, 0, _terminal.Cols, selective);
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

        // ECH honours the ISO guard (SPA/EPA) but not DECSCA -- it is an ordinary erase.
        EraseLineCells(line, _buffer.X, Math.Min(_buffer.X + count, _terminal.Cols), selective: false);
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

    /// <summary>
    /// One tab motion, for both C0 HT and CHT (<c>CSI Ps I</c>), which is n of them.
    /// </summary>
    /// <remarks>
    /// <para>Shared because the two have drifted apart twice. First on the stop set, when HT
    /// hardcoded 8 while CHT honoured <c>Options.TabStopWidth</c>. Then on the PHANTOM
    /// column: HT checked for a pending wrap and CHT did not, so CHT cancelled it by moving
    /// the cursor and the next printable overwrote the last column instead of wrapping onto
    /// the next row. vttest draws the same row of marks with tabs and with CHT and expects
    /// them to look the same; they did not.</para>
    ///
    /// <para>The phantom column is where curses' famous more(1) bug lives: a program fills a
    /// row and then tabs. With DECSET 41 the tab wraps first, as the printed character would
    /// have; without it the tab is absorbed where it stands and the next printable does the
    /// wrapping. Either way the answer is the same for one tab or five.</para>
    /// </remarks>
    internal void Tab(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            if (_buffer.PendingWrap)
            {
                if (!_terminal.MoreFixMode)
                    return;

                if (_buffer.Y == _buffer.ScrollBottom)
                    _buffer.ScrollUp(1);
                else
                    _buffer.SetCursor(_buffer.X, _buffer.Y + 1);

                _buffer.CarriageReturn();
            }

            // The margin binds any cursor at or left of it -- starting left of the left
            // margin tabs INTO the box and stops at its right edge, the same discipline
            // printing follows -- while a cursor already right of the margin only stops at
            // the screen edge.
            var limit = _terminal.LeftRightMarginMode && _buffer.X <= _buffer.ScrollRight
                ? _buffer.ScrollRight
                : _terminal.Cols - 1;

            _buffer.SetCursor(Math.Min(_terminal.NextTabStop(_buffer.X), limit), _buffer.Y);
        }
    }

    /// <summary>
    /// DECREQTPARM (<c>CSI Ps x</c>) - answers with DECREPTPARM.
    /// </summary>
    /// <remarks>
    /// <para>A VT100 asking about its serial line: parity, bit count, transmit and receive
    /// speed, clock multiplier. None of it means anything to an emulator, and the values
    /// below are the ones xterm sends for the same reason -- no parity, 8 bits, the highest
    /// speed code, multiplier 1, no flags.</para>
    ///
    /// <para>Answered anyway because silence is not a decline: vttest's terminal-report test
    /// paints nothing at all against a terminal that says nothing, and a client that blocks
    /// on the reply waits forever. Ps 0 reports with sol=2, Ps 1 with sol=3; anything else
    /// is not a request and is ignored, which is what stops a REPORT arriving on the input
    /// from being answered as though it were one.</para>
    /// </remarks>
    private void RequestTerminalParameters(Params parameters)
    {
        var request = parameters.GetParam(0, 0);
        if (request != 0 && request != 1)
            return;

        var sol = request == 0 ? 2 : 3;
        _terminal.RaiseDataReceived($"\u001b[{sol};1;1;128;128;1;0x");
    }

    private void CursorForwardTab(Params parameters) =>
        Tab(Math.Max(parameters.GetParam(0, 1), 1));

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
            // No margin floor: backward tabs ignore the region entirely -- xterm lets CBT walk
            // straight out of the left margin to column 1.
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
                    // CSI ? row ; col ; page R -- CPR plus the page, which is always 1 here.
                    var row = _buffer.Y + 1; // 1-based
                    // A cursor pending a wrap sits one PAST the last column -- a position no
                    // character occupies and no terminal reports. xterm answers with the last
                    // column, which is what lets a program trust CPR arithmetic at the margin.
                    var col = Math.Min(_buffer.X, _terminal.Cols - 1) + 1; // 1-based
                    if (_terminal.OriginMode)
                    {
                        row -= _buffer.ScrollTop;
                        col -= _buffer.ScrollLeft;
                    }
                    _terminal.RaiseDataReceived($"\u001b[?{row};{col};1R");
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
                    // (language = North American, status = ready, type = LK201)
                    _terminal.RaiseDataReceived("\u001b[?27;1;0;0n");
                    break;

                case 53: // DSR locator status (DEC form)
                case 55: // DSR locator status (xterm form)
                    // 53 = locator available. There is no locator device, but xterm answers
                    // 53 here too -- DECEFR and friends are accepted, just eventless.
                    _terminal.RaiseDataReceived("\u001b[?53n");
                    break;

                case 56: // DSR locator type
                    // CSI ? 57 ; 1 n -- the locator, such as it is, is a mouse.
                    _terminal.RaiseDataReceived("\u001b[?57;1n");
                    break;

                case 62: // DECMSR - Macro Space Report
                    // CSI Pn * { -- no macros, no space for macros. Note: NOT a ?-prefixed reply.
                    _terminal.RaiseDataReceived("\u001b[0*{");
                    break;

                case 63: // DECCKSR - Memory Checksum Report
                    // DCS Pid ! ~ checksum ST, echoing the request's id. No macro memory, so 0.
                    var id = parameters.GetParam(1, 0);
                    _terminal.RaiseDataReceived($"\u001bP{id}!~0000\u001b\\");
                    break;

                case 75: // DSR data integrity
                    // CSI ? 7 0 n -- no communication errors.
                    _terminal.RaiseDataReceived("\u001b[?70n");
                    break;

                case 85: // DSR multiple-session status
                    // CSI ? 8 3 n -- not configured for multiple sessions.
                    _terminal.RaiseDataReceived("\u001b[?83n");
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
                    var col = Math.Min(_buffer.X, _terminal.Cols - 1) + 1; // 1-based, phantom clamped

                    // Origin mode is a coordinate SYSTEM, not a row offset: both axes are
                    // reported relative to the region's top-left, column included.
                    if (_terminal.OriginMode)
                    {
                        row -= _buffer.ScrollTop;
                        col -= _buffer.ScrollLeft;
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
        // DECSLRM is VT400: below level 64 the sequence is SCOSC on some terminals and noise on
        // the rest, and honouring it would give a level-62 program margins it cannot have asked for.
        if (_terminal.ConformanceLevel < 64)
            return;

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

        // The top margin must be ABOVE the bottom one; a request where it is not is ignored
        // whole, keeping the previous region -- not clamped into a one-row band the program
        // never asked for.
        if (top >= bottom)
            return;

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

        // Values of 24 and up are not window operations at all: they are DECSLPP, set the page
        // length to that many lines, kept inside the same final byte since the VT340.
        if (operation >= 24)
        {
            _terminal.Resize(_terminal.Cols, operation);
            return;
        }

        switch (operation)
        {
            case 1: // De-iconify window (restore from minimized)
                if (_terminal.Options.WindowOptions.RestoreWin)
                {
                    _terminal.WindowIconified = false;
                    _terminal.RaiseWindowRestored();
                }
                break;

            case 2: // Iconify window (minimize)
                if (_terminal.Options.WindowOptions.MinimizeWin)
                {
                    _terminal.WindowIconified = true;
                    _terminal.RaiseWindowMinimized();
                }
                break;

            case 3: // Move window to x, y
                if (_terminal.Options.WindowOptions.SetWinPosition)
                {
                    var x = parameters.GetParam(1, 0);
                    var y = parameters.GetParam(2, 0);
                    _terminal.WindowX = x;
                    _terminal.WindowY = y;
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
                    // Response: CSI 1 t (not iconified) or CSI 2 t (iconified). When no host
                    // answers the event, the virtual state winops 1 and 2 maintain does.
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.State);
                    var iconified = args.Handled ? args.IsIconified : _terminal.WindowIconified;
                    _terminal.RaiseDataReceived($"\u001b[{(iconified ? 2 : 1)}t");
                }
                break;

            case 13: // Report window position
                if (_terminal.Options.WindowOptions.GetWinPosition)
                {
                    // Response: CSI 3 ; x ; y t, from the host if it answers, else from the
                    // position winop 3 last set.
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.Position);
                    var x = args.Handled ? args.X : _terminal.WindowX;
                    var y = args.Handled ? args.Y : _terminal.WindowY;
                    _terminal.RaiseDataReceived($"\u001b[3;{x};{y}t");
                }
                break;

            case 14: // Report window size in pixels
                if (_terminal.Options.WindowOptions.GetWinSizePixels)
                {
                    // Answered whether or not a host handler does. An emulator has no pixels
                    // of its own, so zeroes are the honest values -- but silence is not an
                    // answer, and enabling the report is a statement that the question will be
                    // answered. The position report next door already works this way.
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.SizePixels);
                    var height = args.Handled ? args.HeightPixels : 0;
                    var width = args.Handled ? args.WidthPixels : 0;

                    // Response: CSI 4 ; height ; width t
                    _terminal.RaiseDataReceived($"\u001b[4;{height};{width}t");
                }
                break;

            case 15: // Report screen size in pixels
                if (_terminal.Options.WindowOptions.GetScreenSizePixels)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.ScreenSizePixels);
                    var height = args.Handled ? args.HeightPixels : 0;
                    var width = args.Handled ? args.WidthPixels : 0;

                    // Response: CSI 5 ; height ; width t
                    _terminal.RaiseDataReceived($"\u001b[5;{height};{width}t");
                }
                break;

            case 16: // Report character cell size in pixels
                if (_terminal.Options.WindowOptions.GetCellSizePixels)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.CellSizePixels);
                    var height = args.Handled ? args.CellHeight : 0;
                    var width = args.Handled ? args.CellWidth : 0;

                    // Response: CSI 6 ; height ; width t
                    _terminal.RaiseDataReceived($"\u001b[6;{height};{width}t");
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
                    // Response: OSC L label ST. ST, not BEL: this reply answers a CSI query, and
                    // xterm (and esctest's reader) terminate it with ST unconditionally.
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.IconTitle);
                    var label = args.Handled && args.Title != null ? args.Title : _terminal.IconTitle;
                    _terminal.RaiseDataReceived($"\u001b]L{EncodeTitleReport(label)}\u001b\\");
                }
                break;

            case 21: // Report window title
                if (_terminal.Options.WindowOptions.GetWinTitle)
                {
                    // Response: OSC l title ST
                    var title = _terminal.Title ?? string.Empty;
                    _terminal.RaiseDataReceived($"\u001b]l{EncodeTitleReport(title)}\u001b\\");
                }
                break;

            case 22: // Push titles onto the stack
                // One stack, each entry holding BOTH titles regardless of the sub-parameter --
                // xterm's model, and the tests pin it down: pushing icon-and-window then popping
                // just the icon consumes the whole entry, so a following window pop finds the
                // stack empty; pushing icon then window then popping both restores each from the
                // top entry, which snapshotted both titles.
                _titleStack.Add((_terminal.IconTitle, _terminal.Title ?? string.Empty));
                break;

            case 23: // Pop titles from the stack
            {
                if (_titleStack.Count == 0)
                    break;
                // Sub-parameter picks what the popped entry restores: 0 = both, 1 = icon,
                // 2 = window. The entry is consumed either way.
                var which = parameters.GetParam(1, 0);
                var (icon, window) = _titleStack[^1];
                _titleStack.RemoveAt(_titleStack.Count - 1);
                if (which is 0 or 1)
                    _terminal.IconTitle = icon;
                if (which is 0 or 2)
                {
                    _terminal.Title = window;
                    _terminal.RaiseTitleChanged(window);
                }
                break;
            }
        }
    }

    // ESC Handler Implementations

    private void IndexDown()
    {
        if (_buffer.Y == _buffer.ScrollBottom)
        {
            // A cursor OUTSIDE the left/right margins is outside the region: it must neither
            // scroll the region's contents nor step past its bottom row. It just stays -- and the
            // same holds on the screen's last row, where there is nowhere to go either.
            if (!CursorInMarginColumns())
                return;

            _buffer.ScrollUp(1);
        }
        else if (_buffer.Y == _terminal.Rows - 1)
        {
            // Below the region entirely (bottom margin above this row): pinned at the screen edge.
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
            // The mirror of IndexDown: a cursor outside the left/right margins is outside the
            // region, so at the top margin it neither scrolls the region nor climbs past it.
            if (!CursorInMarginColumns())
                return;

            _buffer.ScrollDown(1);
        }
        else if (_buffer.Y == 0)
        {
            // Above the region entirely: pinned at the screen's top edge.
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y - 1);
        }
    }

    /// <summary>
    /// Marks the viewport row <paramref name="row"/> as no longer being a soft-wrap continuation
    /// of the line above it. The erase operations call this where xterm clears its line-wrap
    /// flag; without it, reverse-wraparound would walk the cursor up across a join the program
    /// has since erased.
    /// </summary>
    private void BreakWrapFromAbove(int row)
    {
        if (row >= _terminal.Rows)
            return;
        var line = _buffer.Lines[_buffer.YBase + row];
        if (line is not null)
            line.IsWrapped = false;
    }

    /// <summary>
    /// The DECSET states XTSAVE (CSI ? Pm s) has stashed away, by mode number, waiting for the
    /// matching XTRESTORE (CSI ? Pm r).
    /// </summary>
    private readonly Dictionary<int, bool> _xtermSavedModes = new();

    /// <summary>The XTWINOPS 22/23 title stack; each entry snapshots both titles. See winop 22.</summary>
    private readonly List<(string Icon, string Window)> _titleStack = new();

    /// <summary>
    /// Title modes, CSI &gt; Pm t to set and CSI &gt; Pm T to reset: 0 = titles are SET in hex,
    /// 1 = title REPORTS are hex, 2/3 = the same in UTF-8, which this terminal already is.
    /// </summary>
    private void SetTitleModes(Params parameters, bool enable)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            switch (parameters.GetParam(i, 0))
            {
                case 0:
                    _terminal.TitleSetHex = enable;
                    break;
                case 1:
                    _terminal.TitleQueryHex = enable;
                    break;
                // 2 and 3 select UTF-8 titles; everything here is UTF-8 already, so they hold.
            }
        }
    }

    /// <summary>
    /// Applies the hex-set title mode: returns the argument decoded when the mode is on,
    /// unchanged when off, and null for a hex string too mangled to decode, which xterm drops.
    /// </summary>
    private string? DecodeTitleArgument(string arg)
    {
        if (!_terminal.TitleSetHex)
            return arg;
        if (arg.Length % 2 != 0)
            return null;
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromHexString(arg));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Applies the hex-query title mode to an outgoing label report.</summary>
    private string EncodeTitleReport(string text)
        => _terminal.TitleQueryHex
            ? Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(text))
            : text;

    private void XtermSaveMode(Params parameters)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            var mode = parameters.GetParam(i, 0);
            if (TryGetPrivateModeState(mode, out var set))
                _xtermSavedModes[mode] = set;
        }
    }

    private void XtermRestoreMode(Params parameters)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            var mode = parameters.GetParam(i, 0);
            if (!_xtermSavedModes.TryGetValue(mode, out var set))
                continue;
            if (set)
                SetCSIMode(mode, isPrivate: true);
            else
                ResetCSIMode(mode, isPrivate: true);
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
        var designations = _buffer.SavedCursorState.Designations ??= new (string, bool)[4];
        for (var slot = 0; slot < designations.Length; slot++)
            designations[slot] = DesignationOf((CharsetMode)slot);
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
                RestoreDesignation((CharsetMode)slot, designations[slot]);
        }

        _currentCharset = _buffer.SavedCursorState.Charset;
        RefreshActiveCharset();
        _terminal.OriginMode = _buffer.SavedCursorState.OriginMode;
        _buffer.SetPendingWrap(_buffer.SavedCursorState.PendingWrap);
    }

    /// <summary>Restores the rendition state consumed by printing and background erasure.</summary>
    internal void ResetAttributes() => _curAttr = AttributeData.Default;
    /// <summary>
    /// DECRQCRA -- reports a 16-bit checksum of a rectangular area of the screen, as
    /// <c>DCS Pid ! ~ XXXX ST</c>. The one sequence esctest builds every content assertion on:
    /// it reads single cells back through this, so a terminal without it cannot be conformance-
    /// tested at all.
    /// </summary>
    /// <remarks>
    /// <para>The sum follows the DEC/xterm convention esctest's default expects: each cell
    /// contributes its character's codepoints, a cell that holds nothing contributes a SPACE --
    /// erased and never-written alike, which is also what lets DEC's trailing-blank trimming be
    /// reasoned away by the client -- and the report carries the NEGATED total (0x10000 - sum),
    /// which is what xterm sent before patch #279 and what esctest's default
    /// <c>--xterm-checksum 0</c> undoes on its side.</para>
    /// <para>Attributes deliberately contribute nothing. esctest compares a cell's checksum to
    /// the bare codepoint of the character it expects, so a weight per attribute bit would fail
    /// every assertion on styled text.</para>
    /// <para>The page parameter is accepted and ignored: there is one screen. Coordinates are
    /// 1-based screen positions, clamped, whole screen when omitted.</para>
    /// </remarks>
    private void RequestChecksumRectangularArea(Params parameters)
    {
        var id = parameters.GetParam(0, 0);
        // parameters[1] is the page, ignored. Coordinates are read in the ORIGIN MODE system,
        // like a cursor address and like every rectangle operation: a program that addresses its
        // region relatively asks about it relatively.
        var originX = _terminal.OriginMode ? _buffer.ScrollLeft : 0;
        var originY = _terminal.OriginMode ? _buffer.ScrollTop : 0;
        var top = Math.Max(1, parameters.GetParam(2, 1) + originY);
        var left = Math.Max(1, parameters.GetParam(3, 1) + originX);
        var bottom = Math.Min(_terminal.Rows, parameters.GetParam(4, _terminal.Rows - originY) + originY);
        var right = Math.Min(_terminal.Cols, parameters.GetParam(5, _terminal.Cols - originX) + originX);

        var sum = 0;
        for (var row = top; row <= bottom; row++)
        {
            var line = _buffer.Lines[_buffer.YBase + row - 1];
            if (line is null)
                continue;

            for (var col = left; col <= right && col <= line.Length; col++)
            {
                var cell = line[col - 1];
                var content = cell.Content;
                if (string.IsNullOrEmpty(content))
                {
                    // The trailing half of a wide character is a placeholder, not a blank: its
                    // character was already counted in full one cell to the left.
                    if (cell.Width == 0)
                        continue;
                    sum += 0x20;
                    continue;
                }

                foreach (var ch in content)
                    sum += ch;
            }
        }

        _terminal.RaiseDataReceived($"\u001bP{id}!~{(0x10000 - sum) & 0xFFFF:X4}\u001b\\");
    }
}
