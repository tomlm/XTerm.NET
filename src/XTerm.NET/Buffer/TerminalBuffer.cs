using System.Text;
using XTerm.Common;

namespace XTerm.Buffer;

/// <summary>
/// Main terminal buffer that manages the active screen and scrollback.
/// </summary>
public class TerminalBuffer
{
    private readonly CircularList<BufferLine> _lines;
    private readonly bool _hasScrollback;
    private int _yDisp;
    private int _yBase;
    private int _y;
    private int _x;
    private int _scrollBottom;
    private int _scrollTop;
    private int _cols;
    private int _rows;

    /// <summary>
    /// The absolute line index of the top of the viewport in the buffer.
    /// In XTerm.js this is 'ydisp'. This represents the current scroll position.
    /// </summary>
    public int ViewportY
    {
        get => _yDisp;
        set => _yDisp = Math.Clamp(value, 0, _yBase);
    }

    /// <summary>
    /// The absolute line index where new content is being written.
    /// In XTerm.js this is 'ybase'. This represents the bottom of the active content.
    /// </summary>
    public int BaseY => _yBase;

    /// <summary>
    /// Total number of lines in the buffer (scrollback + active lines).
    /// </summary>
    public int Length => _lines.Length;

    /// <summary>
    /// Whether the viewport is at the bottom (showing latest content).
    /// In xterm.js: ydisp === ybase means we're at the bottom.
    /// </summary>
    public bool IsAtBottom => _yDisp >= _yBase;

    /// <summary>
    /// Number of columns in the buffer.
    /// </summary>
    public int Cols => _cols;

    /// <summary>
    /// Number of rows in the buffer (viewport height).
    /// </summary>
    public int Rows => _rows;

    // Legacy properties for backward compatibility
    public int YDisp => _yDisp;
    public int YBase => _yBase;
    public int Y => _y;
    public int X => _x;
    public int ScrollTop => _scrollTop;
    public int ScrollBottom => _scrollBottom;

    public CircularList<BufferLine> Lines => _lines;

    /// <summary>
    /// Fired when lines are trimmed from the start of the buffer.
    /// </summary>
    public event Action<int>? Trimmed;

    /// <summary>
    /// Whether scrolling reuses the scrollback line it is about to discard instead of allocating a
    /// new one. On by default. Turn it off if a consumer holds <see cref="BufferLine"/> references
    /// across writes; see the invariant documented in <c>ScrollUp</c>.
    /// </summary>
    public bool RecycleScrolledLines { get; set; } = true;

    /// <summary>
    /// Saved cursor state for DECSC/DECRC.
    /// </summary>
    public class SavedCursor
    {
        public int X { get; set; }
        public int Y { get; set; }
        public AttributeData Attr { get; set; }
        public CharsetMode Charset { get; set; }

        public SavedCursor()
        {
            X = 0;
            Y = 0;
            Attr = AttributeData.Default;
            Charset = CharsetMode.G0;
        }
    }

    public SavedCursor SavedCursorState { get; set; }

    public TerminalBuffer(int cols, int rows, int scrollback, bool hasScrollback = true)
    {
        _hasScrollback = hasScrollback;
        _cols = cols;
        _rows = rows;
        _lines = new CircularList<BufferLine>(rows + scrollback);
        _yDisp = 0;
        _yBase = 0;
        _y = 0;
        _x = 0;
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        SavedCursorState = new SavedCursor();

        // Initialize buffer with empty lines
        for (int i = 0; i < rows; i++)
        {
            _lines.Push(new BufferLine(cols, BufferCell.Space));
        }
    }

    private bool IsReflowEnabled => _hasScrollback && _lines.MaxLength > _rows;

    /// <summary>
    /// Gets a line from the buffer.
    /// </summary>
    public BufferLine? GetLine(int y)
    {
        return _lines[y];
    }

    /// <summary>
    /// Gets a blank line (filled with null cells).
    /// </summary>
    public BufferLine GetBlankLine(AttributeData attr, bool isWrapped = false)
    {
        var fillCell = BufferCell.Space;
        fillCell.Attributes = attr;
        return new BufferLine(_cols, fillCell) { IsWrapped = isWrapped };
    }

    /// <summary>
    /// Scrolls the buffer up by a specified number of lines.
    /// This matches xterm.js Buffer.scroll() behavior.
    /// </summary>
    public void ScrollUp(int lines, bool isWrapped = false)
    {
        for (int i = 0; i < lines; i++)
        {
            BufferLine newLine;

            // Only the full-screen scroll region contributes to scrollback.
            // Top-anchored partial regions reserve rows below the margin and
            // must scroll in place so prompts/status rows are not promoted.
            if (_scrollTop == 0 && _scrollBottom == _rows - 1 && _lines.MaxLength > _rows)
            {
                // Reuse the line the ring is about to drop, rather than allocating a replacement.
                //
                // At capacity, Push overwrites the oldest slot -- so the oldest line becomes garbage
                // on every single scrolled line. At 240 columns that is several KB per line, and it
                // showed up as ~19 gen0 collections per million characters on short-line output.
                // Handing that same line back as the new blank one turns the allocation into a fill.
                //
                // INVARIANT this relies on: nothing outside the buffer retains a BufferLine across a
                // write. Within this library that holds -- SelectionManager tracks coordinates and
                // adjusts them on Trimmed, it does not hold line objects. Renderers must fetch lines
                // from Buffer.Lines per frame rather than caching them; per-line render state belongs
                // in BufferLine.Cache, which ResetInPlace clears. Set RecycleScrolledLines to false
                // if a consumer cannot honour that.
                if (RecycleScrolledLines
                    && _lines.TryPeekEvictionCandidate(out var recycled)
                    && recycled.Length == _cols)
                {
                    var fill = BufferCell.Space;
                    fill.Attributes = AttributeData.Default;
                    recycled.ResetInPlace(fill, isWrapped);
                    newLine = recycled;
                }
                else
                {
                    newLine = GetBlankLine(AttributeData.Default, isWrapped);
                }

                // When scrollTop is 0, the top line goes into scrollback.
                // In xterm.js: push new line first, then increment yBase and yDisp.
                // This causes the circular list to potentially recycle the oldest line.

                // Check if we're at max capacity - if so, yBase stays the same but 
                // the buffer rotates. If not, yBase increments.
                var willBeRecycled = _lines.Length >= _lines.MaxLength;

                // Push the new line at the end (bottom of screen in buffer terms)
                _lines.Push(newLine);

                if (willBeRecycled)
                {
                    Trimmed?.Invoke(1);
                }

                // Only increment yBase if the buffer didn't recycle
                if (!willBeRecycled)
                {
                    _yBase++;
                }

                // If yDisp was at the bottom, keep it there
                if (_yDisp + 1 < _yBase)
                {
                    // User was scrolled up, don't auto-scroll
                }
                else
                {
                    _yDisp = _yBase;
                }
            }
            else
            {
                // A partial scroll region drops a line from the middle of the ring, not the oldest
                // slot, so there is nothing safe to recycle here.
                newLine = GetBlankLine(AttributeData.Default, isWrapped);

                // Scroll region is not at top of screen.
                // Remove line from scroll region top and add blank at bottom.
                // Use yBase offset for correct absolute positioning.
                var scrollRegionStart = _yBase + _scrollTop;
                var scrollRegionEnd = _yBase + _scrollBottom;

                // Delete the line at the top of scroll region
                _lines.Splice(scrollRegionStart, 1);

                // Insert blank line at bottom of scroll region
                _lines.Splice(scrollRegionEnd, 0, newLine);
            }
        }
    }

    /// <summary>
    /// Scrolls the buffer down by a specified number of lines.
    /// This is reverse scrolling within the scroll region.
    /// </summary>
    public void ScrollDown(int lines)
    {
        for (int i = 0; i < lines; i++)
        {
            // Calculate absolute positions in the buffer
            var scrollRegionStart = _yBase + _scrollTop;
            var scrollRegionEnd = _yBase + _scrollBottom;

            // Remove line from scroll region bottom
            _lines.Splice(scrollRegionEnd, 1);

            // Add blank line at top of scroll region
            var newLine = GetBlankLine(AttributeData.Default);
            _lines.Splice(scrollRegionStart, 0, newLine);
        }
    }

    /// <summary>
    /// Scrolls the display by a specified amount.
    /// This only changes the viewport position, not the buffer content.
    /// </summary>
    public void ScrollDisp(int disp, bool suppressScrollEvent = false)
    {
        _yDisp = Math.Clamp(_yDisp + disp, 0, _yBase);
    }

    /// <summary>
    /// Scrolls the viewport to show a specific line.
    /// </summary>
    /// <param name="line">The absolute line number to scroll to</param>
    public void ScrollToLine(int line)
    {
        _yDisp = Math.Clamp(line, 0, _yBase);
    }

    /// <summary>
    /// Scrolls the display to the bottom (showing active screen).
    /// In xterm.js, yDisp = yBase means showing the active terminal area.
    /// </summary>
    public void ScrollToBottom()
    {
        _yDisp = _yBase;
    }

    /// <summary>
    /// Scrolls the display to the top.
    /// </summary>
    public void ScrollToTop()
    {
        _yDisp = 0;
    }

    /// <summary>
    /// Discards the scrollback — every line above the visible screen — leaving the visible screen and the
    /// cursor untouched.
    /// </summary>
    /// <remarks>
    /// <para>This is what <c>CSI 3 J</c> asks for, and it is a different operation from erasing: the lines
    /// are REMOVED from the buffer rather than blanked, so the history is genuinely gone and cannot be
    /// scrolled back to.</para>
    /// <para><c>_yBase</c> and <c>_yDisp</c> must move with the lines. They are absolute indices into the
    /// buffer, so trimming from the start without adjusting them leaves the visible screen indexed at an
    /// offset that no longer exists, and the next write runs off the end of the list.</para>
    /// </remarks>
    /// <summary>
    /// Drops every image in the buffer, leaving the cells that held them as blanks.
    /// </summary>
    /// <remarks>
    /// The cells keep their attributes, so a picture drawn over a coloured background leaves that
    /// background behind rather than a hole. The images themselves are collected once their last
    /// reference goes.
    /// </remarks>
    public void ClearImages()
    {
        for (int i = 0; i < _lines.Length; i++)
        {
            _lines[i]?.ClearImages();
        }
    }

    public void ClearScrollback()
    {
        if (_yBase == 0)
            return;

        _lines.TrimStart(_yBase);
        _yBase = 0;
        _yDisp = 0;
    }

    /// <summary>
    /// Scrolls the viewport by a relative number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll (negative = up, positive = down)</param>
    public void ScrollLines(int lines)
    {
        ScrollToLine(_yDisp + lines);
    }

    /// <summary>
    /// Sets the scroll region.
    /// </summary>
    public void SetScrollRegion(int top, int bottom)
    {
        _scrollTop = Math.Clamp(top, 0, _rows - 1);
        _scrollBottom = Math.Clamp(bottom, _scrollTop, _rows - 1);
    }

    /// <summary>
    /// Resets the scroll region to full screen.
    /// </summary>
    public void ResetScrollRegion()
    {
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    /// <summary>
    /// Gets the absolute line index for a viewport-relative y coordinate.
    /// </summary>
    public int GetAbsoluteY(int y)
    {
        return _yBase + y;
    }

    /// <summary>
    /// Whether a block taller than one row has ever been written to this buffer.
    /// </summary>
    /// <remarks>
    /// The guard in front of <see cref="TryGetSizedRunCovering"/>, which the print path asks per
    /// character. Almost no session ever sets it, and one that does pays a bounded walk of at most
    /// <see cref="Common.TextSizing.MaxScale"/> minus one lines. It answers "is this worth looking
    /// for" rather than counting: a stale false would lose the skipping behaviour entirely, so it is
    /// only ever set by writing a tall block and cleared by
    /// <see cref="RefreshMultiRowSizedRuns"/>, which the operations that can remove the last one
    /// call.
    /// </remarks>
    public bool HasMultiRowSizedRuns { get; internal set; }

    /// <summary>
    /// The OSC 66 block, anchored on an EARLIER row, whose cells cover
    /// <paramref name="column"/> of <paramref name="absoluteRow"/>.
    /// </summary>
    /// <remarks>
    /// <para>A block <c>s</c> cells tall occupies <c>s</c> rows growing downwards from the line that
    /// describes it. Only the first of those rows holds the run; this is how the others are found,
    /// and it is what both the print path and a renderer need — the former to skip over cells that
    /// belong to a block, the latter to know not to draw its own text there.</para>
    /// <para>Rows below the first are found by looking UP rather than by marking them, so nothing
    /// has to be kept in step with a buffer that scrolls: scrolling moves a block and the rows it
    /// covers together, and their adjacency is the whole of the relationship.</para>
    /// </remarks>
    /// <param name="absoluteRow">Row to test, as an index into <see cref="Lines"/>.</param>
    /// <param name="column">Column to test.</param>
    /// <param name="run">The covering block.</param>
    /// <param name="anchorRow">The row the block is anchored on, which is always above.</param>
    public bool TryGetSizedRunCovering(int absoluteRow, int column, out LineSizedRun run, out int anchorRow)
    {
        for (var above = 1; above < Common.TextSizing.MaxScale; above++)
        {
            var row = absoluteRow - above;
            if (row < 0)
                break;

            var line = row < _lines.Length ? _lines[row] : null;
            if (line is null || !line.HasSizedRuns)
                continue;

            if (line.TryGetSizedRunAt(column, out var candidate) && candidate.Rows > above)
            {
                run = candidate;
                anchorRow = row;
                return true;
            }
        }

        run = default;
        anchorRow = -1;
        return false;
    }

    /// <summary>
    /// Erases every OSC 66 block anchored ABOVE <paramref name="absoluteRow"/> whose cells reach
    /// into the given columns of that row, blanking what is left of each on its own line.
    /// </summary>
    /// <remarks>
    /// The protocol's erase rule is about the region of the screen erased, not about a line: a block
    /// intersected anywhere -- including on a row it merely hangs over -- is erased whole. The
    /// controls that erase a line's own cells get that for free, because writing over any cell of a
    /// block destroys it; this is the other half, for the rows below the block's own.
    /// </remarks>
    public void EraseSizedRunsCovering(int absoluteRow, int column, int count)
    {
        if (!HasMultiRowSizedRuns || count <= 0)
            return;

        for (var above = 1; above < Common.TextSizing.MaxScale; above++)
        {
            var row = absoluteRow - above;
            if (row < 0)
                break;

            var line = row < _lines.Length ? _lines[row] : null;
            if (line is null || !line.HasSizedRuns)
                continue;

            line.EraseSizedRunsReaching(column, count, above);
        }
    }

    /// <summary>
    /// Re-derives <see cref="HasMultiRowSizedRuns"/> from what the buffer actually holds.
    /// </summary>
    /// <remarks>
    /// The flag only ever turns itself on, because turning it off wrongly would lose the skipping
    /// behaviour while turning it on wrongly costs only a lookup. That leaves it to the operations
    /// that plausibly remove the last tall block -- clearing the screen, a reset -- to ask for it to
    /// be worked out again, which is worth doing because a stale true retires the print fast path
    /// for the rest of the session. The scan is bounded by the ring and only runs when the flag is
    /// set, so a session that has never drawn a tall block never pays for it.
    /// </remarks>
    public void RefreshMultiRowSizedRuns()
    {
        if (!HasMultiRowSizedRuns)
            return;

        for (int i = 0; i < _lines.Length; i++)
        {
            var line = _lines[i];
            if (line is null || !line.HasSizedRuns)
                continue;

            foreach (var run in line.SizedRuns)
            {
                if (run.Rows > 1)
                    return;
            }
        }

        HasMultiRowSizedRuns = false;
    }

    /// <summary>
    /// Resizes the buffer.
    /// </summary>
    public void Resize(int newCols, int newRows)
    {
        // Only a wrap chain drops its pictures now. Reflow re-wraps a logical line by copying
        // ranges of cells between lines, and a run anchored to a column would end up describing
        // content that is no longer there.
        //
        // Every other width change is free. A run keeps its NATURAL width and the renderer draws as
        // much of it as the line allows, so narrowing shows less of a picture and widening shows
        // more — with nothing destroyed and nothing to restore. Dropping every image on any width
        // change, as this did, lost pictures on the most common resize there is.
        if (newCols != _cols)
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                var line = _lines[i];
                if (line is null || !line.HasImages)
                    continue;

                var next = i + 1 < _lines.Length ? _lines[i + 1] : null;
                if (line.IsWrapped || next is { IsWrapped: true })
                    line.ClearImages();
            }
        }

        var nullCell = BufferCell.Space;
        var newMaxLength = newRows + (_lines.MaxLength - _rows);

        if (newMaxLength > _lines.MaxLength)
        {
            _lines.Resize(newMaxLength);
        }

        if (_lines.Length > 0 && _cols < newCols)
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i]?.Resize(newCols, nullCell);
            }
        }

        // Outside the "has lines" guard on purpose. A buffer built with zero rows -- which the
        // constructor allows -- could never be brought to life by a later resize while this sat
        // inside it: Lines.Length stayed 0 and the next write indexed an empty list.
        while (_lines.Length < newRows)
        {
            _lines.Push(new BufferLine(newCols, nullCell));
        }

        // Growing the window pulls scrollback lines back into view, which is this clamp forcing
        // YBase down. The CURSOR has to ride along: its position is YBase + Y, so every line YBase
        // gives back must be added to Y, or the cursor slides UP the content by that much. A window
        // dragged taller then has the shell's SIGWINCH redraws stamping prompts down through
        // whatever the cursor slid over, one line per resize event.
        var yBaseBefore = _yBase;
        _yBase = Math.Min(_yBase, Math.Max(0, _lines.Length - newRows));
        _y += yBaseBefore - _yBase;
        _yDisp = Math.Clamp(_yDisp, 0, _yBase);

        if (_lines.Length > 0)
        {
            if (IsReflowEnabled && newCols != _cols)
            {
                if (newCols > _cols)
                {
                    ReflowLarger(newCols, newRows);
                }
                else
                {
                    ReflowSmaller(newCols, newRows);
                }
            }

            if (_cols > newCols)
            {
                for (int i = 0; i < _lines.Length; i++)
                {
                    _lines[i]?.Resize(newCols, nullCell);
                }
            }
        }

        var oldRows = _rows;
        _cols = newCols;
        _rows = newRows;

        if (_scrollBottom == oldRows - 1)
        {
            _scrollBottom = newRows - 1;
        }
        else
        {
            _scrollBottom = Math.Min(_scrollBottom, newRows - 1);
        }
        _scrollTop = Math.Min(_scrollTop, newRows - 1);

        // Clamp, not Min. Moving to the NEW column count was the point of this change, but dropping
        // the lower bound with it meant a negative cursor -- which SetCursorRaw exists to allow --
        // survived the resize and left the buffer reporting an out-of-bounds position.
        _x = Math.Clamp(_x, 0, Math.Max(0, newCols - 1));
        // The mirror case. A cursor below the new bottom is NOT simply clamped into place -- its
        // overflow is pushed into scrollback, so the cursor stays on the LINE it was on. Clamping
        // alone moved the cursor onto earlier content: shrink a window with a prompt at row 22 down
        // to ten rows and the cursor landed on absolute row 9, where the next write destroyed
        // whatever lived there.
        // Floored, because newRows can be zero: a bare "newRows - 1" is -1 there, which makes the
        // test true for any cursor and inflates the overflow by one, scrolling the buffer during a
        // resize that has no viewport at all.
        var newBottom = Math.Max(0, newRows - 1);

        // The screen is the last `rows` lines of the buffer, so a shrink has to move the difference
        // into scrollback. Shifting only enough to bring the cursor on screen left lines stranded
        // BELOW the screen, where scrolling cannot reach them -- the viewport tops out at _yBase.
        // So shift as far toward the tail as there is room for, stopping at the cursor: the cursor
        // must not end up above the screen, and keeping it on its line is what this is all for.
        if (_y > newBottom)
        {
            var overflow = _y - newBottom;
            var room = Math.Max(0, _lines.Length - newRows - _yBase);
            var wasFollowing = _yDisp == _yBase;

            // At least enough to bring the cursor back on screen. But when the viewport was
            // following the tail, take all the room there is -- bounded by the cursor, which must
            // not end up above the screen. Shifting the bare minimum left lines stranded BELOW the
            // screen, where scrolling cannot reach them, because the viewport tops out at _yBase.
            var shift = Math.Min(room, wasFollowing ? _y : overflow);

            _yBase += shift;
            _y -= shift;
            if (wasFollowing)
                _yDisp = _yBase;
        }

        _y = Math.Clamp(_y, 0, newBottom);
        SavedCursorState.X = Math.Clamp(SavedCursorState.X, 0, Math.Max(0, newCols - 1));
        SavedCursorState.Y = Math.Max(SavedCursorState.Y, 0);

        if (newMaxLength < _lines.MaxLength)
        {
            var amountToTrim = _lines.Length - newMaxLength;
            if (amountToTrim > 0)
            {
                // Whether the viewport was following the bottom has to be read BEFORE the trim,
                // because afterwards there is nothing left to tell from.
                var wasFollowingBottom = _yDisp == _yBase;

                _lines.TrimStart(amountToTrim);
                Trimmed?.Invoke(amountToTrim);

                // Recomputed against the trimmed buffer rather than shifted by the trim amount.
                // Shifting kept the viewport a fixed distance from rows that are no longer there:
                // a 5-row buffer with 5 of scrollback resized to 3 rows ended up showing rows 3..5
                // of 8, with the live bottom sitting unseen at row 7, and everything written
                // afterwards landing outside the visible area.
                _yBase = Math.Max(0, _lines.Length - _rows);
                _yDisp = wasFollowingBottom
                    ? _yBase
                    : Math.Clamp(_yDisp - amountToTrim, 0, _yBase);
                SavedCursorState.Y = Math.Max(SavedCursorState.Y - amountToTrim, 0);
            }
            _lines.Resize(newMaxLength);
        }
    }

    private void ReflowLarger(int newCols, int newRows)
    {
        var nullCell = BufferCell.Space;
        var toRemove = BufferReflow.ReflowLargerGetLinesToRemove(
            _lines, _cols, newCols, _yBase + _y, nullCell);
        if (toRemove.Length > 0)
        {
            var newLayoutResult = BufferReflow.ReflowLargerCreateNewLayout(_lines, toRemove);
            BufferReflow.ReflowLargerApplyNewLayout(_lines, newLayoutResult.Layout);
            ReflowLargerAdjustViewport(newCols, newRows, newLayoutResult.CountRemoved);
        }
    }

    private void ReflowLargerAdjustViewport(int newCols, int newRows, int countRemoved)
    {
        var nullCell = BufferCell.Space;
        var viewportAdjustments = countRemoved;
        while (viewportAdjustments-- > 0)
        {
            if (_yBase == 0)
            {
                if (_y > 0)
                {
                    _y--;
                }
                if (_lines.Length < newRows)
                {
                    _lines.Push(new BufferLine(newCols, nullCell));
                }
            }
            else
            {
                if (_yDisp == _yBase)
                {
                    _yDisp--;
                }
                _yBase--;
            }
        }
        SavedCursorState.Y = Math.Max(SavedCursorState.Y - countRemoved, 0);
    }

    private void ReflowSmaller(int newCols, int newRows)
    {
        var nullCell = BufferCell.Space;
        var toInsert = new List<(int Start, List<BufferLine> NewLines)>();
        var countToInsert = 0;

        for (var y = _lines.Length - 1; y >= 0; y--)
        {
            // Bounds-checked, because this loop's own body shrinks _lines: the viewport adjustment
            // below calls Pop, so y can be past the end by the next iteration. The reference
            // implementation is JavaScript, where reading past the end yields undefined and lands in
            // the null check underneath; in C# the same read throws.
            var nextLine = y < _lines.Length ? _lines[y] : null;
            if (nextLine == null || (!nextLine.IsWrapped && nextLine.GetTrimmedLength() <= newCols))
            {
                continue;
            }

            var wrappedLines = new List<BufferLine> { nextLine };
            while (nextLine.IsWrapped && y > 0)
            {
                nextLine = _lines[--y]!;
                wrappedLines.Insert(0, nextLine);
            }

            if (BufferReflow.IsUnreflowable(wrappedLines))
            {
                continue;
            }

            var absoluteY = _yBase + _y;
            if (absoluteY >= y && absoluteY < y + wrappedLines.Count)
            {
                continue;
            }

            var lastLineLength = wrappedLines[^1].GetTrimmedLength();
            var destLineLengths = BufferReflow.ReflowSmallerGetNewLineLengths(wrappedLines, _cols, newCols);
            if (destLineLengths.Length == 0)
            {
                // A wrapped group holding nothing at all. ReflowSmallerGetNewLineLengths loops while
                // cellsAvailable < cellsNeeded, so a group whose trimmed length is zero produces an
                // EMPTY array, and reading [Length - 1] from it below throws.
                //
                // Only a one-row group can be empty: GetWrappedLineTrimmedLength returns cols for
                // every row of a group except the last, so anything with two rows already counts a
                // full row of cells. A one-row group means a continuation row sitting at index 0
                // with an unwrapped row beneath it, which is what is left once the row it continued
                // has been trimmed out of the scrollback -- and it is blank whenever the wrap was
                // over whitespace. There is nothing to redistribute either way.
                continue;
            }
            var linesToAdd = destLineLengths.Length - wrappedLines.Count;
            int trimmedLines;
            if (_yBase == 0 && _y != _lines.Length - 1)
            {
                trimmedLines = Math.Max(0, _y - _lines.MaxLength + linesToAdd);
            }
            else
            {
                trimmedLines = Math.Max(0, _lines.Length - _lines.MaxLength + linesToAdd);
            }

            var newLines = new List<BufferLine>();
            for (var i = 0; i < linesToAdd; i++)
            {
                newLines.Add(GetBlankLine(AttributeData.Default, isWrapped: true));
            }

            if (newLines.Count > 0)
            {
                toInsert.Add((y + wrappedLines.Count + countToInsert, newLines));
                countToInsert += newLines.Count;
            }

            wrappedLines.AddRange(newLines);

            var destLineIndex = destLineLengths.Length - 1;
            var destCol = destLineLengths[destLineIndex];
            if (destCol == 0)
            {
                destLineIndex--;
                destCol = destLineLengths[destLineIndex];
            }

            var srcLineIndex = wrappedLines.Count - linesToAdd - 1;
            var srcCol = lastLineLength;
            while (srcLineIndex >= 0)
            {
                var cellsToCopy = Math.Min(srcCol, destCol);
                if (wrappedLines[destLineIndex] == null)
                {
                    break;
                }

                wrappedLines[destLineIndex].CopyCellsFrom(
                    wrappedLines[srcLineIndex], srcCol - cellsToCopy, destCol - cellsToCopy, cellsToCopy, true);
                destCol -= cellsToCopy;
                if (destCol == 0)
                {
                    destLineIndex--;
                    if (destLineIndex < 0)
                    {
                        break;
                    }
                    destCol = destLineLengths[destLineIndex];
                }
                srcCol -= cellsToCopy;
                if (srcCol == 0)
                {
                    srcLineIndex--;
                    var wrappedLinesIndex = Math.Max(srcLineIndex, 0);
                    srcCol = BufferReflow.GetWrappedLineTrimmedLength(wrappedLines, wrappedLinesIndex, _cols);
                }
            }

            for (var i = 0; i < wrappedLines.Count && i < destLineLengths.Length; i++)
            {
                if (destLineLengths[i] < newCols)
                {
                    wrappedLines[i].ReplaceCells(destLineLengths[i], newCols, nullCell);
                }
            }

            var viewportAdjustments = linesToAdd - trimmedLines;
            while (viewportAdjustments-- > 0)
            {
                if (_yBase == 0)
                {
                    if (_y < newRows - 1)
                    {
                        _y++;
                        _lines.Pop();
                    }
                    else
                    {
                        _yBase++;
                        _yDisp++;
                    }
                }
                else
                {
                    if (_yBase < Math.Min(_lines.MaxLength, _lines.Length + countToInsert) - newRows)
                    {
                        if (_yBase == _yDisp)
                        {
                            _yDisp++;
                        }
                        _yBase++;
                    }
                }
            }

            SavedCursorState.Y = Math.Min(SavedCursorState.Y + linesToAdd, _yBase + newRows - 1);
        }

        if (toInsert.Count > 0)
        {
            var originalLines = new List<BufferLine>(_lines.Length);
            for (var i = 0; i < _lines.Length; i++)
            {
                originalLines.Add(_lines[i]!);
            }

            var originalLinesLength = originalLines.Count;
            RebuildWithInsertions(originalLines, toInsert, countToInsert);

            var amountToTrim = Math.Max(0, originalLinesLength + countToInsert - _lines.MaxLength);
            if (amountToTrim > 0)
            {
                _yBase = Math.Max(_yBase - amountToTrim, 0);
                _yDisp = Math.Max(_yDisp - amountToTrim, 0);
                SavedCursorState.Y = Math.Max(SavedCursorState.Y - amountToTrim, 0);
                Trimmed?.Invoke(amountToTrim);
            }
        }
    }

    private void RebuildWithInsertions(
        IReadOnlyList<BufferLine> originalLines,
        IReadOnlyList<(int Start, List<BufferLine> NewLines)> toInsert,
        int countInserted)
    {
        var originalLinesLength = originalLines.Count;
        _lines.SetLength(Math.Min(_lines.MaxLength, originalLinesLength + countInserted));

        var originalLineIndex = originalLinesLength - 1;
        var nextToInsertIndex = 0;
        var nextToInsert = nextToInsertIndex < toInsert.Count ? toInsert[nextToInsertIndex] : ((int Start, List<BufferLine> NewLines)?)null;
        var countInsertedSoFar = 0;

        for (var i = Math.Min(_lines.MaxLength - 1, originalLinesLength + countInserted - 1); i >= 0; i--)
        {
            if (nextToInsert.HasValue && nextToInsert.Value.Start > originalLineIndex + countInsertedSoFar)
            {
                var insert = nextToInsert.Value;

                // i >= 0 guards the destination. A line that expands by more than the remaining
                // capacity produces more rows than there are slots, and without this the loop ran
                // i down past zero and threw. Rows that do not fit are the OLDEST, which is what
                // capacity trimming discards anyway.
                for (var nextI = insert.NewLines.Count - 1; nextI >= 0 && i >= 0; nextI--)
                {
                    _lines[i--] = insert.NewLines[nextI];
                }
                i++;

                countInsertedSoFar += insert.NewLines.Count;
                nextToInsertIndex++;
                nextToInsert = nextToInsertIndex < toInsert.Count ? toInsert[nextToInsertIndex] : null;
            }
            else
            {
                // Same guard on the source. Once the originals are exhausted there is nothing left
                // to place, and the remaining slots already hold inserted rows.
                if (originalLineIndex < 0)
                {
                    break;
                }

                _lines[i] = originalLines[originalLineIndex--];
            }
        }
    }

    /// <summary>
    /// Sets the cursor position.
    /// </summary>
    public void SetCursor(int x, int y)
    {
        _x = Math.Clamp(x, 0, _cols - 1);
        _y = Math.Clamp(y, 0, _rows - 1);
    }

    /// <summary>
    /// Moves the cursor to the specified position without any clamping.
    /// </summary>
    public void SetCursorRaw(int x, int y)
    {
        _x = x;
        _y = y;
    }

    public string PrintViewport()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < _rows; i++)
        {
            var line = GetLine(_yDisp + i);
            if (line != null)
            {
                foreach (var cell in line)
                {
                    sb.Append(cell.Content);
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
