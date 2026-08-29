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
    private int _scrollLeft;
    private int _scrollRight;
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

    /// <summary>The leftmost column of the scrolling region. Zero unless DECSLRM narrowed it.</summary>
    public int ScrollLeft => _scrollLeft;

    /// <summary>The rightmost column of the scrolling region. The last column unless DECSLRM narrowed it.</summary>
    public int ScrollRight => _scrollRight;

    /// <summary>
    /// True while the scrolling region spans every column, which is the ordinary case.
    /// </summary>
    /// <remarks>
    /// Worth testing before anything else, because a full-width region scrolls by moving whole LINES
    /// through the ring -- which is what feeds the scrollback and what the line recycling depends on.
    /// Narrowed margins cannot use that path at all: only part of each line moves, the rest stays,
    /// and nothing is promoted to scrollback. Two implementations, and this decides between them.
    /// </remarks>
    public bool MarginsAreFullWidth => _scrollLeft == 0 && _scrollRight >= _cols - 1;

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
        _scrollLeft = 0;
        _scrollRight = cols - 1;
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
        // The signature promises null for a row that is not there; forwarding straight to the ring
        // threw IndexOutOfRangeException instead, so every caller written against the nullable
        // contract was one stale row index away from taking down the write loop.
        if (y < 0 || y >= _lines.Length)
            return null;

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
    /// Scrolls the scrolling region up, dispatching on the margins: full-width margins move whole
    /// lines through the ring, narrowed margins move only the margin columns as a box.
    /// </summary>
    /// <param name="lines">How many rows to scroll by.</param>
    /// <param name="isWrapped">
    /// Whether the line this scroll makes room for continues the previous one. Honoured only on
    /// the full-width path: a box scroll neither sets nor clears any line's flag, for the reasons
    /// the body explains.
    /// </param>
    /// <remarks>
    /// The full-width path matches xterm.js <c>Buffer.scroll()</c> — promotion to scrollback
    /// included. None of that applies to the narrowed path: xterm.js has no left/right margins,
    /// no lines move through the ring, and nothing reaches scrollback.
    /// </remarks>
    public void ScrollUp(int lines, bool isWrapped = false)
    {
        // Decided here rather than at each call site. Every path that scrolls -- a wrap at the
        // bottom of the region, LF, IND, DECSTBM's own scroll -- arrives through this, so putting
        // the choice anywhere else means finding all of them and finding them again next time.
        //
        // isWrapped is DELIBERATELY not forwarded. It is a per-LINE flag -- "this line continues
        // the previous one" -- and a margin scroll moves a column BOX, not lines: every line in
        // the region keeps its content outside the margins, so marking the bottom line wrapped
        // would claim continuation for content that never moved, and a later reflow would merge
        // full lines an application laid out separately. There is no per-line value that can
        // describe a box continuation, so the flags of the untouched outside content win, and a
        // box scroll neither sets nor clears any line's flag.
        if (!MarginsAreFullWidth)
        {
            ScrollMarginColumns(_scrollTop, _scrollBottom, lines, up: true, BlankFill());
            return;
        }

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

                // The splice below is what IL and DL do, so it owes any block it tears the same.
                // Flag tested HERE: a DECSTBM region scrolls once per newline — the tmux shape —
                // and no bench corpus walks this path to catch a method call per line.
                if (HasMultiRowSizedRuns)
                    EraseSizedRunsSplitBy(scrollRegionStart, scrollRegionEnd);

                // Delete the line at the top of scroll region
                _lines.Splice(scrollRegionStart, 1);

                // Insert blank line at bottom of scroll region
                _lines.Splice(scrollRegionEnd, 0, newLine);
            }
        }
    }

    /// <summary>
    /// Scrolls the scrolling region down — reverse scrolling — with the same dispatch as
    /// <see cref="ScrollUp"/>: whole lines when the margins are full width, a column box when
    /// they are narrowed. Nothing reaches scrollback in either direction.
    /// </summary>
    public void ScrollDown(int lines)
    {
        if (!MarginsAreFullWidth)
        {
            ScrollMarginColumns(_scrollTop, _scrollBottom, lines, up: false, BlankFill());
            return;
        }

        for (int i = 0; i < lines; i++)
        {
            // Calculate absolute positions in the buffer
            var scrollRegionStart = _yBase + _scrollTop;
            var scrollRegionEnd = _yBase + _scrollBottom;

            if (HasMultiRowSizedRuns)
                EraseSizedRunsSplitBy(scrollRegionStart, scrollRegionEnd);

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

        var dropped = _yBase;
        _lines.TrimStart(dropped);
        _yBase = 0;
        _yDisp = 0;

        // Anything tracking absolute rows -- a selection, a search result, a shell-integration
        // mark -- is now pointing above the buffer. Resize's trim path already reports itself;
        // this one did not, so CSI 3 J left the selection highlighting rows that had moved.
        Trimmed?.Invoke(dropped);
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

        // The columns too. RIS and DECSTR both come through here, and leaving margins in
        // force across a full reset would hand the next application a region it never asked for and
        // has no reason to check.
        ResetLeftRightMargins();
    }

    /// <summary>
    /// Sets the left and right margins (DECSLRM). Both are inclusive and zero-based.
    /// </summary>
    /// <returns>False when the pair is degenerate, in which case nothing is changed.</returns>
    /// <remarks>
    /// A right margin at or left of the left one is refused rather than clamped. DEC requires the
    /// region to be at least two columns wide, and clamping a nonsense pair into a legal one leaves
    /// an application drawing into a region it did not ask for and cannot detect -- whereas ignoring
    /// it leaves the previous margins in force, which the application can at least query.
    /// </remarks>
    public bool SetLeftRightMargins(int left, int right)
    {
        left = Math.Clamp(left, 0, _cols - 1);
        right = Math.Clamp(right, 0, _cols - 1);

        if (right <= left)
            return false;

        _scrollLeft = left;
        _scrollRight = right;
        return true;
    }

    /// <summary>Widens the margins back to the whole screen.</summary>
    public void ResetLeftRightMargins()
    {
        _scrollLeft = 0;
        _scrollRight = _cols - 1;
    }

    /// <summary>
    /// Moves the margin columns of rows <paramref name="top"/>..<paramref name="bottom"/> by
    /// <paramref name="count"/> rows, filling what it vacates. The COLUMNS are not parameters:
    /// they are always the current left/right margins. Only the rows vary, and only because IL
    /// and DL scroll from the cursor's row rather than from the top of the region — which is why
    /// this is internal, not public: the row parameters are an implementation detail of its four
    /// callers (<see cref="ScrollUp"/>, <see cref="ScrollDown"/>, IL and DL), not an API.
    /// </summary>
    /// <remarks>
    /// <para>The narrowed-margin half of all four of those operations, and a different operation
    /// rather than a parameter on them. A full-width scroll moves whole LINES through the ring: the
    /// top line is promoted to scrollback and a blank one appended. Only part of each line moves
    /// here, so the lines stay where they are and their cells are copied between them -- and nothing
    /// reaches the scrollback, because half a line is not a line anyone could scroll back to.</para>
    /// <para>Copying row by row in the direction of travel, so a region taller than the distance
    /// moved does not overwrite what it has yet to read.</para>
    /// </remarks>
    internal void ScrollMarginColumns(int top, int bottom, int count, bool up, BufferCell fill)
    {
        if (count <= 0 || top > bottom)
            return;

        var left = _scrollLeft;
        var width = _scrollRight - _scrollLeft + 1;
        if (width <= 0)
            return;

        var rows = bottom - top + 1;
        count = Math.Min(count, rows);

        // A box scroll moves cells between lines while the runs describing them stay behind, so any
        // OSC 66 block inside the box -- or hanging into it from above -- is erased first. The same
        // rule the line-splicing scrolls follow, applied to the columns this one actually moves.
        if (HasMultiRowSizedRuns || AnySizedRunsIn(top, bottom))
        {
            for (var row = top; row <= bottom; row++)
            {
                EraseSizedRunsCovering(_yBase + row, left, width);
                _lines[_yBase + row]?.EraseSizedRunsReaching(left, width, 0);
            }

            RefreshMultiRowSizedRuns();
        }

        if (up)
        {
            for (var row = top; row <= bottom - count; row++)
                CopyMarginColumns(row + count, row, left, width);

            for (var row = bottom - count + 1; row <= bottom; row++)
                FillMarginColumns(row, left, width, fill);
        }
        else
        {
            for (var row = bottom; row >= top + count; row--)
                CopyMarginColumns(row - count, row, left, width);

            for (var row = top; row < top + count; row++)
                FillMarginColumns(row, left, width, fill);
        }
    }

    /// <summary>Whether any line in the given viewport rows carries a sized run.</summary>
    private bool AnySizedRunsIn(int top, int bottom)
    {
        for (var row = top; row <= bottom; row++)
        {
            if (_lines[_yBase + row] is { HasSizedRuns: true })
                return true;
        }

        return false;
    }

    /// <summary>The blank a scroll leaves behind, matching what the full-width path fills with.</summary>
    private static BufferCell BlankFill()
    {
        var cell = BufferCell.Space;
        cell.Attributes = AttributeData.Default;
        return cell;
    }

    private void CopyMarginColumns(int fromRow, int toRow, int left, int width)
    {
        var from = _lines[_yBase + fromRow];
        var to = _lines[_yBase + toRow];
        if (from is null || to is null)
            return;

        to.CopyCellsFrom(from, left, left, width, false);
    }

    private void FillMarginColumns(int row, int left, int width, BufferCell fill)
    {
        var line = _lines[_yBase + row];
        line?.Fill(fill, left, left + width);
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
    /// Erases every OSC 66 block that a splice of the rows <paramref name="regionStart"/> to
    /// <paramref name="regionEnd"/> would tear in half, and re-derives the search flag.
    /// </summary>
    /// <remarks>
    /// <para>A scroll of a PARTIAL region is the same buffer transformation <c>IL</c> and <c>DL</c>
    /// perform: a line is spliced out of the middle of the ring and a blank spliced in. Rows inside
    /// the region move together, so a block wholly inside one keeps its shape; the two blocks that
    /// do not survive are the one hanging INTO the region from above, whose lower rows are about to
    /// move away from it, and the one anchored inside the region that reaches OUT below it, whose
    /// lower rows are about to stay behind. Both are split, and the protocol has a split block
    /// erased.</para>
    /// <para>A full-screen scroll moves every row together and needs none of this, which is why the
    /// call sites ask only for the region they splice.</para>
    /// </remarks>
    internal void EraseSizedRunsSplitBy(int regionStart, int regionEnd)
    {
        if (!HasMultiRowSizedRuns)
            return;

        EraseSizedRunsCovering(regionStart, 0, _cols);

        for (var row = regionStart; row <= regionEnd; row++)
        {
            var line = row >= 0 && row < _lines.Length ? _lines[row] : null;
            if (line is null || !line.HasSizedRuns)
                continue;

            // A block on this row reaches past the region when it is taller than the rows left
            // below it, the last of which is the region's own bottom.
            line.EraseSizedRunsReaching(0, _cols, regionEnd - row + 1);
        }

        RefreshMultiRowSizedRuns();
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
        var oldCols = _cols;
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

        // The same for the columns. A right margin that reached the old edge follows the new one --
        // an application that asked for "everything" should keep getting everything -- and a
        // narrower one is clamped in. If the clamp makes the pair degenerate, the margins go back to
        // the whole screen rather than leaving a region no write could land in.
        if (_scrollRight >= oldCols - 1)
            _scrollRight = newCols - 1;
        else
            _scrollRight = Math.Min(_scrollRight, Math.Max(0, newCols - 1));

        _scrollLeft = Math.Min(_scrollLeft, Math.Max(0, newCols - 1));

        if (_scrollRight <= _scrollLeft)
            ResetLeftRightMargins();

        // Clamp, not Min. Moving to the NEW column count was the point of this change, but dropping
        // the lower bound with it meant a negative cursor -- which SetCursorRaw exists to allow --
        // survived the resize and left the buffer reporting an out-of-bounds position.
        _x = Math.Clamp(_x, 0, Math.Max(0, newCols - 1));
        PendingWrap = false;
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
    /// True while the cursor's position is the residue of PRINTING — which is the only way it
    /// comes to rest one past the last column it wrote, the pending-wrap state.
    /// </summary>
    /// <remarks>
    /// Exists because that one-past column is otherwise two different states with one
    /// representation: X == ScrollRight + 1 is both "just filled the region's last column, wrap
    /// due at the margin" and "deliberately placed at the first column right of the margin" — an
    /// ordinary place for a cursor in the split layouts DECSLRM exists for. xterm keeps a
    /// separate wrap flag so its column is never ambiguous; this is that flag, mapped onto the
    /// existing seam: <see cref="SetCursorRaw"/> is how printing advances the cursor and sets it,
    /// <see cref="SetCursor"/> is how everything else moves the cursor and clears it, matching
    /// xterm's rule that any explicit movement cancels a pending wrap. When X is inside the
    /// margins the flag is meaningless and harmlessly stale; only the boundary column reads it.
    /// </remarks>
    public bool PendingWrap { get; private set; }

    /// <summary>
    /// Moves the cursor to the line’s start as CR defines it: the LEFT MARGIN when the cursor is
    /// at or right of it, column 0 when the cursor is left of it. xterm’s rule (CarriageReturn in
    /// charproc.c), independent of origin mode — a cursor inside the region cannot escape it
    /// leftward, and one already left of the margin was never in the region to begin with. Every
    /// operation that “returns the carriage” — CR itself, NEL, CNL/CPL, and a line feed under
    /// ConvertEol — routes through this so none of them can disagree about where a line starts.
    /// Clears the pending wrap, as any deliberate movement does.
    /// </summary>
    public void CarriageReturn() => SetCursor(_x < _scrollLeft ? 0 : _scrollLeft, _y);

    /// <summary>
    /// Sets the cursor position — the deliberate, clamped move every cursor-addressing sequence
    /// uses, which is why it cancels a pending wrap.
    /// </summary>
    public void SetCursor(int x, int y)
    {
        _x = Math.Clamp(x, 0, _cols - 1);
        _y = Math.Clamp(y, 0, _rows - 1);
        PendingWrap = false;
    }

    /// <summary>
    /// Moves the cursor without clamping — the print-path move, which is what may leave X one
    /// past the last written column and therefore sets <see cref="PendingWrap"/>.
    /// </summary>
    public void SetCursorRaw(int x, int y)
    {
        _x = x;
        _y = y;
        PendingWrap = true;
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
