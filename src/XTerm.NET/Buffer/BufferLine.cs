using System.Collections;
using System.Text;

namespace XTerm.Buffer;

/// <summary>
/// Represents a single line in the terminal buffer.
/// Contains an array of cells and metadata about the line.
/// </summary>
public class BufferLine : IEnumerable<BufferCell>
{
    private BufferCell[] _cells;

    /// <summary>
    /// The picture runs shown on this line, or null — which is every line, in almost every session.
    /// </summary>
    /// <remarks>
    /// This is where a picture LIVES. Cells carry no image data at all, so nothing about a picture
    /// is destroyed by anything that truncates or overwrites cells, and a resize needs to do nothing
    /// to images whatsoever: the renderer draws as much of each run as the current width allows.
    /// </remarks>
    private List<Graphics.LinePlacement>? _placements;

    /// <summary>
    /// Shell-integration marks on this line, or null — which is every line that is not a prompt.
    /// </summary>
    private List<LineMark>? _marks;

    /// <summary>
    /// OSC 8 link spans on this line, or null — which is nearly every line.
    /// </summary>
    private List<LineHyperlink>? _links;

    /// <summary>
    /// OSC 66 sized runs on this line, or null — which is nearly every line.
    /// </summary>
    private List<LineSizedRun>? _sizedRuns;

    /// <summary>
    /// The images those runs refer to, held strongly so they stay alive exactly as long as this line
    /// does — so a picture scrolled off the end of the scrollback dies with the last line showing it,
    /// with no eviction pass and nothing to keep in step with a buffer that scrolls.
    /// </summary>
    private List<Graphics.TerminalImage>? _images;
    private int _length;
    private bool _isWrapped;
    private LineAttribute _lineAttribute;

    public int Length => _length;

    public bool IsWrapped
    {
        get => _isWrapped;
        set => _isWrapped = value;
    }

    /// <summary>
    /// Gets or sets the DEC line attribute (double-width/double-height).
    /// Set via ESC # sequences: ESC # 3 (top), ESC # 4 (bottom), ESC # 5 (normal), ESC # 6 (double-width).
    /// </summary>
    public LineAttribute LineAttribute
    {
        get => _lineAttribute;
        set
        {
            _lineAttribute = value;
            Cache = null;
        }
    }

    /// <summary>
    /// Returns true if this line has a double-width attribute (DECDWL or DECDHL).
    /// Double-width lines can only display cols/2 characters.
    /// </summary>
    public bool IsDoubleWidth => _lineAttribute.IsDoubleWidth();

    /// <summary>
    /// Cache object - this will be cleared on writes to the bufferline.
    /// </summary>
    public object? Cache { get; set; }

    public BufferLine(int cols, BufferCell? fillCell = null)
    {
        _length = cols;
        _cells = new BufferCell[cols];
        _isWrapped = false;
        _lineAttribute = LineAttribute.Normal;

        var fill = fillCell ?? BufferCell.Space;
        for (int i = 0; i < cols; i++)
        {
            _cells[i] = fill;
        }
        Cache = null;
    }

    /// <summary>
    /// Gets or sets a cell at a specific column.
    /// </summary>
    public BufferCell this[int index]
    {
        get
        {
            if (index < 0 || index >= _length)
                return BufferCell.Empty;
            return _cells[index];
        }
        set
        {
            if (index >= 0 && index < _length)
            {
                _cells[index] = value;
                Cache = null;
            }
        }
    }

    /// <summary>
    /// Sets a cell at a specific column.
    /// </summary>
    /// <summary>
    /// Whether this line has ever held a two-column character. A latch, not a count: it exists so
    /// the print path can skip its orphan check with one field read, and the only cost of a stale
    /// true is doing a check that finds nothing. Clearing it accurately would mean scanning the
    /// row on every erase, which is the work it was added to avoid.
    /// </summary>
    public bool HasWideCells { get; private set; }

    /// <summary>Blanks either half of a wide character that the range [start, end) would orphan.</summary>
    private void RepairAround(int start, int end)
    {
        if (start > 0 && start < _length && GetWidth(start - 1) == 2)
        {
            var orphan = BufferCell.Space;
            orphan.Attributes = _cells[start - 1].Attributes;
            _cells[start - 1] = orphan;
        }

        if (end < _length && end > 0 && _cells[end - 1].Width == 2)
        {
            var orphan = BufferCell.Space;
            orphan.Attributes = _cells[end].Attributes;
            _cells[end] = orphan;
        }
    }

    public void SetCell(int index, ref BufferCell cell)
    {
        if (index >= 0 && index < _length)
        {
            if (cell.Width == 2)
                HasWideCells = true;

            _cells[index] = cell;

            // Printing over a Sixel picture replaces that part of it. With tiles in cells this
            // happened for free; with runs it is explicit. One field test on the overwhelmingly
            // common line, which has no pictures at all.
            if (_placements is not null)
                SplitPlacementsAt(index);

            Cache = null;
        }
    }

    /// <summary>
    /// Writes a run of single-width, single-codepoint cells starting at <paramref name="index"/>.
    ///
    /// The per-cell SetCell path re-checks bounds and clears Cache for every character. Now that
    /// BufferCell holds no references, a run can be written straight into a span of the backing
    /// array: bounds are checked once, the cache is cleared once, and the writes carry no GC write
    /// barrier. Callers must have established that every char is printable, single-width and needs
    /// no charset translation.
    /// </summary>
    public void SetSingleWidthRun(int index, ReadOnlySpan<char> text, AttributeData attributes)
    {
        if (index < 0 || text.Length == 0 || index + text.Length > _length)
            return;

        // Same invariant Fill keeps, for the same reason: a run landing on either half of a wide
        // character must take the other half with it, or the renderer draws a two-column glyph
        // into one column. Behind the latch, because this is the ASCII path -- a line that never
        // held a wide cell cannot orphan one, and two array reads per run were measurable on it.
        if (HasWideCells)
            RepairAround(index, index + text.Length);

        var cells = _cells.AsSpan(index, text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            cells[i].CodePoint = text[i];
            cells[i].Width = 1;
            cells[i].Attributes = attributes;
            cells[i].ClusterId = XTerm.Common.ClusterTable.None;
        }

        // This path writes cells directly rather than through SetCell, so it has to do what SetCell
        // does about pictures: printing over one replaces that part of it. Missing this made text
        // typed over an image leave the image behind, but only when the fast path took the write --
        // which is the sort of difference between two paths that reads as an intermittent fault.
        //
        // One field test on a line with no pictures, which is nearly every line.
        if (_placements is not null)
            SplitPlacementsOver(index, text.Length);

        Cache = null;
    }

    /// <summary>
    /// As above, from UTF-8 bytes. Every byte must be printable ASCII, where the byte value and the
    /// codepoint are the same number, so no decoding is needed at all.
    /// </summary>
    public void SetSingleWidthRun(int index, ReadOnlySpan<byte> ascii, AttributeData attributes)
    {
        if (index < 0 || ascii.Length == 0 || index + ascii.Length > _length)
            return;

        var cells = _cells.AsSpan(index, ascii.Length);
        for (var i = 0; i < ascii.Length; i++)
        {
            cells[i].CodePoint = ascii[i];
            cells[i].Width = 1;
            cells[i].Attributes = attributes;
            cells[i].ClusterId = XTerm.Common.ClusterTable.None;
        }

        // Same as the char overload: this writes cells directly, so it owes pictures the same
        // treatment SetCell gives them.
        if (_placements is not null)
            SplitPlacementsOver(index, ascii.Length);

        Cache = null;
    }

    /// <summary>
    /// Gets the cell code point at a specific column.
    /// </summary>
    public int GetCodePoint(int index)
    {
        if (index >= 0 && index < _length)
            return _cells[index].CodePoint;
        return 0;
    }

    /// <summary>
    /// Resizes the line to a new column count.
    /// </summary>
    public void Resize(int cols, BufferCell fillCell)
    {
        if (cols == _length)
            return;

        var oldLength = _length;

        if (cols > _length)
        {
            var newCells = new BufferCell[cols];
            Array.Copy(_cells, newCells, _length);
            for (int i = _length; i < cols; i++)
            {
                newCells[i] = fillCell;
            }
            _cells = newCells;
        }
        else
        {
            var newCells = new BufferCell[cols];
            Array.Copy(_cells, newCells, cols);
            _cells = newCells;
        }
        Cache = null;
        _length = cols;

        // A block cut in half by a narrowing does not survive it: the run says which columns hold
        // which part of a scaled glyph, and the columns past the new width are gone. What is left of
        // such a block becomes spaces rather than a first cell still claiming columns that no longer
        // exist. A block that still fits is untouched -- widening moves no cell, and reflow leaves a
        // group holding a run alone precisely so that stays true.
        if (_sizedRuns is not null && cols < oldLength)
            EraseSizedRunsOver(cols, oldLength - cols, blankAll: true);
    }

    /// <summary>
    /// Fills a range of cells with a specific cell.
    /// </summary>
    public void Fill(BufferCell fillCell, int startCol = 0, int endCol = -1)
    {
        if (endCol == -1)
            endCol = _length;

        // A wide character straddles two columns, so a range that cuts through one leaves the
        // other half behind: a width-2 cell whose second column is now a space, or a spacer with
        // nothing in front of it. The renderer then draws a two-column glyph into one column and
        // the rest of the row shifts. ReplaceCells has carried this repair all along and only
        // reflow ever reached it; erasing needs it just as much, and widening the range here also
        // gives the link, image and sized-run bookkeeping below the true span that was cleared.
        if (HasWideCells)
        {
            if (startCol > 0 && startCol < _length && GetWidth(startCol - 1) == 2)
                startCol--;

            if (endCol < _length && endCol > 0 && GetWidth(endCol - 1) == 2)
                endCol++;
        }

        for (int i = startCol; i < endCol && i < _length; i++)
        {
            _cells[i] = fillCell;
        }

        // Erasing takes any picture in the span with it -- overlays included, unlike printing.
        if (_placements is not null)
            SplitPlacementsOver(startCol, Math.Max(0, Math.Min(endCol, _length) - startCol),
                                includeOverlays: true);

        // And a LINK, unlike a mark. A mark records a position in the history and survives the
        // shell redrawing its prompt; a link is a property of its text, and text that has been
        // erased cannot be the thing a URL was attached to -- leaving it would keep an invisible
        // span of the screen clickable.
        if (_links is not null)
            SplitLinksOver(startCol, Math.Max(0, Math.Min(endCol, _length) - startCol));

        // And a sized run, for the same reason and then some: the cells it described are now blank,
        // so a run left behind would have a renderer drawing scaled text over an erased span.
        if (_sizedRuns is not null)
            EraseSizedRunsOver(startCol, Math.Max(0, Math.Min(endCol, _length) - startCol));

        Cache = null;
    }

    /// <summary>
    /// Copies cells from another line.
    /// </summary>
    public void CopyCellsFrom(BufferLine src, int srcCol, int destCol, int length, bool applyInReverse)
    {
        // Memmove semantics, decided HERE rather than trusted to the caller: copying within one
        // line with the destination ahead of the source re-reads cells it has already written
        // when it walks forward, and the shifted region degenerates into the first cell repeated
        // -- which is exactly what a mid-line insert looked like on screen (type into the middle
        // of a bash command line and the tail becomes one letter, over and over). Every caller
        // that shifts right within a line is safe now whatever it passes.
        if (ReferenceEquals(src, this) && destCol > srcCol && destCol < srcCol + length)
            applyInReverse = true;

        if (applyInReverse)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                if (destCol + i < _length && srcCol + i < src._length)
                {
                    _cells[destCol + i] = src._cells[srcCol + i];
                }
            }
        }
        else
        {
            for (int i = 0; i < length; i++)
            {
                if (destCol + i < _length && srcCol + i < src._length)
                {
                    _cells[destCol + i] = src._cells[srcCol + i];
                }
            }
        }
        Cache = null;
    }

    /// <summary>
    /// Translates the line to a string.
    /// </summary>
    public string TranslateToString(bool trimRight = false, int startCol = 0, int endCol = -1)
    {
        if (endCol == -1)
            endCol = _length;

        var sb = new StringBuilder();
        for (int i = startCol; i < endCol && i < _length; i++)
        {
            var cell = _cells[i];
            sb.Append(cell.Content);
        }

        if (trimRight)
        {
            return sb.ToString().TrimEnd();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the width of the cell at the given column.
    /// </summary>
    public int GetWidth(int index)
    {
        if (index < 0 || index >= _length)
            return 1;
        return _cells[index].Width;
    }

    /// <summary>
    /// Returns whether the cell at the given column has content.
    /// </summary>
    public bool HasContent(int index)
    {
        if (index < 0 || index >= _length)
            return false;
        var cell = _cells[index];
        return !cell.IsSpace() && !cell.IsEmpty();
    }

    /// <summary>
    /// Replaces cells in the range [startCol, endCol) with the fill cell.
    /// </summary>
    public void ReplaceCells(int startCol, int endCol, BufferCell fillCell)
    {
        if (startCol > 0 && GetWidth(startCol - 1) == 2)
        {
            _cells[startCol - 1] = fillCell;
        }
        if (endCol < _length && GetWidth(endCol - 1) == 2)
        {
            _cells[endCol] = fillCell;
        }
        while (startCol < endCol && startCol < _length)
        {
            _cells[startCol++] = fillCell;
        }
        Cache = null;
    }

    /// <summary>
    /// Drops any image pieces on this line, leaving the cells blank but otherwise untouched.
    /// </summary>
    /// <returns>True if the line held any, which is also the signal that it needs repainting.</returns>
    public bool ClearImages()
    {
        if (_placements is null && _images is null)
            return false;

        // Nothing to clean up in the cells — they never held anything. Releasing the strong
        // references is what actually frees the pixels.
        _placements = null;
        _images = null;
        Cache = null;
        return true;
    }

    /// <summary>
    /// Whether this line shows any part of a picture. One field test — a renderer can ask per row.
    /// </summary>
    public bool HasImages => _placements is { Count: > 0 };

    /// <summary>Whether this line carries any shell-integration mark.</summary>
    public bool HasMarks => _marks is { Count: > 0 };

    /// <summary>
    /// The shell-integration marks on this line, in the order they were emitted.
    /// </summary>
    public IReadOnlyList<LineMark> Marks
        => (IReadOnlyList<LineMark>?)_marks ?? Array.Empty<LineMark>();

    /// <summary>
    /// Records a mark at a column.
    /// </summary>
    /// <remarks>
    /// A line collects several: a prompt emits A and then B, and a command that produces no output
    /// finishes on the same line it started on.
    /// </remarks>
    internal void AddMark(LineMark mark)
    {
        _marks ??= new List<LineMark>(1);
        _marks.Add(mark);
    }

    /// <summary>Drops every mark. Only line reuse does this; see <see cref="ResetInPlace"/>.</summary>
    internal void ClearMarks() => _marks = null;

    /// <summary>Whether this line carries any OSC 8 link span.</summary>
    public bool HasLinks => _links is { Count: > 0 };

    /// <summary>The link spans on this line, left to right.</summary>
    public IReadOnlyList<LineHyperlink> Links
        => (IReadOnlyList<LineHyperlink>?)_links ?? Array.Empty<LineHyperlink>();

    /// <summary>
    /// The link covering <paramref name="column"/>, if any. What hit-testing a click asks.
    /// </summary>
    public bool TryGetLinkAt(int column, out LineHyperlink link)
    {
        if (_links is not null)
        {
            for (int i = 0; i < _links.Count; i++)
            {
                if (_links[i].Covers(column))
                {
                    link = _links[i];
                    return true;
                }
            }
        }

        link = default;
        return false;
    }

    /// <summary>Drops every link span for line reuse or reflow reconstruction.</summary>
    internal void ClearLinks() => _links = null;

    /// <summary>Adds an already-normalized link span, joining a contiguous piece of the same link.</summary>
    internal void AddLink(LineHyperlink link)
    {
        if (_links is { Count: > 0 })
        {
            var previous = _links[^1];
            if (previous.EndColumn == link.Column && previous.SameLinkAs(link.Url, link.Id))
            {
                _links[^1] = new LineHyperlink(
                    previous.Column, previous.Cols + link.Cols, previous.Url, previous.Id);
                return;
            }
        }

        _links ??= new List<LineHyperlink>(1);
        _links.Add(link);
    }

    /// <summary>
    /// Records what a write of <paramref name="count"/> columns at <paramref name="column"/> did to
    /// this line's links: extended one, started one, or took those columns out of one.
    /// </summary>
    /// <remarks>
    /// <para>Called from the print paths rather than from <c>SetCell</c>, because only the printer
    /// knows whether a link was in force for the text it just wrote — the cell itself carries
    /// nothing about it, which is the point.</para>
    /// <para>Guard the call, not just the body: callers test
    /// <c>url is not null || line.HasLinks</c> first, so a line with no links being written without
    /// one costs a field read rather than a call. That method is too big to inline, and the same
    /// mistake cost the alt-redraw corpus 12% earlier in this project's history.</para>
    /// </remarks>
    internal void NoteLinkRun(int column, int count, string? url, string? id)
    {
        if (count <= 0)
            return;

        if (url is null)
        {
            SplitLinksOver(column, count);
            return;
        }

        // Join a span that continues the one before it, so a link reads as one run rather than one
        // per character. Only the last is worth testing: printing moves left to right.
        if (_links is { Count: > 0 })
        {
            var last = _links[^1];
            if (last.EndColumn == column && last.SameLinkAs(url, id))
            {
                _links[^1] = new LineHyperlink(last.Column, last.Cols + count, url, id);
                return;
            }
        }

        // Anything already claiming these columns loses them -- text written over a link is not
        // part of it, even when a different link is being written.
        SplitLinksOver(column, count);

        _links ??= new List<LineHyperlink>(1);
        _links.Add(new LineHyperlink(column, count, url, id));
    }

    /// <summary>
    /// Takes a written span out of any link covering it, splitting one that was written through.
    /// </summary>
    private void SplitLinksOver(int column, int count)
    {
        if (_links is null)
            return;

        var end = column + count;

        for (int i = _links.Count - 1; i >= 0; i--)
        {
            var link = _links[i];
            if (link.Column >= end || link.EndColumn <= column)
                continue;

            _links.RemoveAt(i);

            var at = i;
            if (link.Column < column)
                _links.Insert(at++, new LineHyperlink(link.Column, column - link.Column, link.Url, link.Id));

            // Inserted in place, NOT appended. Links is documented left to right, and NoteLinkRun's
            // join-the-last-span optimisation relies on the last entry being the rightmost -- an
            // appended fragment from a split in the middle would break both.
            if (link.EndColumn > end)
                _links.Insert(at, new LineHyperlink(end, link.EndColumn - end, link.Url, link.Id));
        }

        if (_links.Count == 0)
            _links = null;
    }

    /// <summary>Whether this line carries any OSC 66 sized run.</summary>
    public bool HasSizedRuns => _sizedRuns is { Count: > 0 };

    /// <summary>The sized runs on this line, left to right.</summary>
    public IReadOnlyList<LineSizedRun> SizedRuns
        => (IReadOnlyList<LineSizedRun>?)_sizedRuns ?? Array.Empty<LineSizedRun>();

    /// <summary>
    /// The sized run covering <paramref name="column"/>, if any. What a renderer asks per cell.
    /// </summary>
    public bool TryGetSizedRunAt(int column, out LineSizedRun run)
    {
        if (_sizedRuns is not null)
        {
            for (int i = 0; i < _sizedRuns.Count; i++)
            {
                if (_sizedRuns[i].Covers(column))
                {
                    run = _sizedRuns[i];
                    return true;
                }
            }
        }

        run = default;
        return false;
    }

    /// <summary>Drops every sized run. Only line reuse does this.</summary>
    internal void ClearSizedRuns() => _sizedRuns = null;

    /// <summary>
    /// Records what a write of <paramref name="count"/> columns at <paramref name="column"/> did to
    /// this line's sized runs, and records the run itself when <paramref name="sizing"/> asks for one.
    /// </summary>
    /// <remarks>
    /// <para>Called from the print paths, as <see cref="NoteLinkRun"/> is, and for the same reason:
    /// only the printer knows what sizing was in force for the text it just wrote.</para>
    /// <para>Adjacent columns with identical sizing are joined into one span. Where one scaled block
    /// ends and the next begins is recoverable from the cells — the first cell of a block carries its
    /// full width and the rest are zero-width — so the span does not have to say it twice.</para>
    /// </remarks>
    internal void NoteSizedRun(int column, int count, Common.TextSizing sizing)
    {
        if (count <= 0)
            return;

        // Anything already claiming these columns is destroyed, not trimmed: overwriting any part of
        // a multicell character erases the whole of it, which is what the protocol requires and what
        // keeps the cells consistent -- a partly overwritten block would leave a first cell still
        // claiming columns that now hold something else.
        EraseSizedRunsOver(column, count);

        if (sizing.IsDefault)
            return;

        // Join a span that continues the one before it. Only the last is worth testing: printing
        // moves left to right.
        if (_sizedRuns is { Count: > 0 })
        {
            var last = _sizedRuns[^1];
            if (last.EndColumn == column && last.Sizing == sizing)
            {
                _sizedRuns[^1] = new LineSizedRun(last.Column, last.Cols + count, sizing);
                return;
            }
        }

        _sizedRuns ??= new List<LineSizedRun>(1);
        _sizedRuns.Add(new LineSizedRun(column, count, sizing));
    }

    /// <summary>
    /// Erases every sized run from <paramref name="column"/> to the end of the line, blanking all of
    /// their cells. What the controls that SHIFT cells owe a multicell: a block cannot survive being
    /// moved out from under the run that describes where it is.
    /// </summary>
    internal void EraseSizedRunsFrom(int column)
        => EraseSizedRunsOver(column, Math.Max(0, _length - column), blankAll: true);

    /// <summary>
    /// Erases every sized run on this line that both touches the given columns and is tall enough to
    /// reach <paramref name="rowsBelow"/> rows down, blanking all of their cells.
    /// </summary>
    /// <remarks>
    /// What a line further down owes the blocks hanging over it. The protocol's erase rule is about
    /// the REGION of the screen erased rather than about a line, so a block whose lower rows are
    /// inside that region dies with it even though its own line was never touched.
    /// </remarks>
    /// <param name="rowsBelow">How far below this line the erased row is; 1 is the next line down.</param>
    internal void EraseSizedRunsReaching(int column, int count, int rowsBelow)
        => EraseSizedRunsOver(column, count, blankAll: true, reachingRows: rowsBelow);

    /// <summary>
    /// Erases every sized run touching the given columns, blanking the cells of theirs that the
    /// caller is not about to write itself.
    /// </summary>
    /// <param name="blankAll">
    /// Whether to blank the whole of each erased run, rather than leaving the columns the caller is
    /// about to write itself. Callers that only overwrite part of the range -- a shift, a delete --
    /// pass true, so no orphaned continuation cell is left behind.
    /// </param>
    /// <param name="reachingRows">
    /// When above zero, only runs drawn over MORE than this many rows are erased -- the caller is a
    /// row below this line, and a run that does not reach it is none of its business.
    /// </param>
    private void EraseSizedRunsOver(int column, int count, bool blankAll = false, int reachingRows = 0)
    {
        if (_sizedRuns is null)
            return;

        var end = column + count;

        for (int i = _sizedRuns.Count - 1; i >= 0; i--)
        {
            var run = _sizedRuns[i];
            if (run.Column >= end || run.EndColumn <= column)
                continue;

            if (run.Rows <= reachingRows)
                continue;

            _sizedRuns.RemoveAt(i);

            // What is left of the block becomes spaces. Its own attributes are kept, so a run erased
            // out of a coloured background does not punch a hole in it.
            for (int c = Math.Max(0, run.Column); c < run.EndColumn && c < _length; c++)
            {
                if (!blankAll && c >= column && c < end)
                    continue;

                var blank = BufferCell.Space;
                blank.Attributes = _cells[c].Attributes;
                _cells[c] = blank;
            }

            Cache = null;
        }

        if (_sizedRuns.Count == 0)
            _sizedRuns = null;
    }

    /// <summary>
    /// The distinct images this line shows.
    /// </summary>
    /// <remarks>
    /// The list to walk when the question is "which pictures are on this line" — asking column by
    /// column both costs more and answers wrongly, because a column covered by two overlapping runs
    /// reports only the first, and an image seen through no other column would be missed entirely.
    /// </remarks>
    public IReadOnlyList<Graphics.TerminalImage> Images
        => (IReadOnlyList<Graphics.TerminalImage>?)_images ?? Array.Empty<Graphics.TerminalImage>();

    /// <summary>The picture runs on this line, in the order they were placed.</summary>
    public IReadOnlyList<Graphics.LinePlacement> Placements
        => (IReadOnlyList<Graphics.LinePlacement>?)_placements ?? Array.Empty<Graphics.LinePlacement>();

    /// <summary>
    /// The run covering <paramref name="column"/>, if any.
    /// </summary>
    /// <remarks>
    /// This is what replaces asking a CELL about its image. A cell is a struct with no idea which
    /// line or column it came from, so it cannot answer for a run anchored to both — the question
    /// can only be asked here. Linear over the runs, of which a line has one or a handful.
    /// </remarks>
    public bool TryGetPlacementAt(int column, out Graphics.LinePlacement placement)
    {
        if (_placements is not null)
        {
            for (int i = 0; i < _placements.Count; i++)
            {
                if (_placements[i].Covers(column))
                {
                    placement = _placements[i];
                    return true;
                }
            }
        }

        placement = default;
        return false;
    }

    /// <summary>
    /// The image shown at <paramref name="column"/>, if any.
    /// </summary>
    /// <remarks>
    /// Resolved from the line's own strong references, so a caller gets the picture without knowing
    /// that ids exist and without touching a weak table it might race.
    /// </remarks>
    public bool TryGetImageAt(int column, out Graphics.TerminalImage image)
    {
        if (TryGetPlacementAt(column, out var placement) && _images is not null)
        {
            foreach (var held in _images)
            {
                if (held.Id == placement.ImageId)
                {
                    image = held;
                    return true;
                }
            }
        }

        image = null!;
        return false;
    }

    /// <summary>
    /// Drops the strong reference to any image this line no longer shows.
    /// </summary>
    /// <remarks>
    /// <para>Ownership is derived from the runs, not tracked alongside them, so anything that
    /// removes a run has to rebuild it. Otherwise a line keeps a picture alive that nothing on it
    /// displays any more — and worse, the budget sweep walks runs to decide what is live, so such a
    /// picture is invisible to it and can never be reclaimed.</para>
    /// <para>Linear in runs times images, both of which are one or a handful.</para>
    /// </remarks>
    private void PruneImages()
    {
        if (_images is null)
            return;

        for (int i = _images.Count - 1; i >= 0; i--)
        {
            var id = _images[i].Id;
            var stillShown = false;

            if (_placements is not null)
            {
                foreach (var placement in _placements)
                {
                    if (placement.ImageId == id)
                    {
                        stillShown = true;
                        break;
                    }
                }
            }

            if (!stillShown)
                _images.RemoveAt(i);
        }

        if (_images.Count == 0)
            _images = null;
    }

    /// <summary>
    /// Removes every run showing one of <paramref name="doomed"/>, leaving the rest alone.
    /// </summary>
    /// <returns>True if anything was removed, which is also the signal to repaint.</returns>
    /// <remarks>
    /// Selective on purpose. Clearing the whole line because one of its pictures was doomed would
    /// take the others with it, which is more destructive than the per-cell code this replaced.
    /// </remarks>
    internal bool RemoveImages(HashSet<Graphics.TerminalImage> doomed)
    {
        if (_placements is null || _images is null)
            return false;

        var doomedIds = new HashSet<int>();
        foreach (var image in _images)
        {
            if (doomed.Contains(image))
                doomedIds.Add(image.Id);
        }

        if (doomedIds.Count == 0)
            return false;

        var removed = _placements.RemoveAll(p => doomedIds.Contains(p.ImageId)) > 0;
        if (!removed)
            return false;

        if (_placements.Count == 0)
            _placements = null;

        PruneImages();
        Cache = null;
        return true;
    }

    /// <summary>Adds a run to this line and takes ownership of the image it shows.</summary>
    internal void AddPlacement(Graphics.LinePlacement placement, Graphics.TerminalImage image)
    {
        if (placement.Cols <= 0)
            return;

        _placements ??= new List<Graphics.LinePlacement>(1);
        _placements.Add(placement);

        _images ??= new List<Graphics.TerminalImage>(1);
        foreach (var held in _images)
        {
            if (ReferenceEquals(held, image))
            {
                Cache = null;
                return;
            }
        }

        _images.Add(image);
        Cache = null;
    }

    /// <summary>
    /// Removes every run matching a test, and releases any image no longer shown by this line.
    /// </summary>
    /// <remarks>
    /// What Kitty's delete matrix removes things through. Deleting by image goes through
    /// <see cref="RemoveImages"/> instead, which is the same operation stated the other way round;
    /// this one answers "which RUNS", which is what selecting by placement id, z-index or position
    /// needs.
    /// </remarks>
    /// <returns>True if anything went, which is also the signal that the line needs repainting.</returns>
    internal bool RemovePlacements(Func<Graphics.LinePlacement, bool> predicate)
    {
        if (_placements is null)
            return false;

        if (_placements.RemoveAll(p => predicate(p)) == 0)
            return false;

        if (_placements.Count == 0)
            _placements = null;

        PruneImages();
        Cache = null;
        return true;
    }

    /// <summary>
    /// Splits any Sixel run covering <paramref name="column"/> around the text just written there.
    /// </summary>
    /// <remarks>
    /// <para>Sixel semantics: printing replaces that part of the picture. With tiles in cells this
    /// happened for free, because the write overwrote the cell; with runs it has to be done on
    /// purpose. The run becomes the fragments either side, each with its source rectangle narrowed
    /// to match, so the rest of the picture survives a character landing in the middle of it.</para>
    /// <para>Kitty runs are left alone — there the z-index decides what is on top, and text never
    /// modifies a placement.</para>
    /// <para>Guarded on a null field at every call site, so a line without pictures — which is
    /// nearly every line — pays a single test.</para>
    /// </remarks>
    internal void SplitPlacementsAt(int column, bool includeOverlays = false)
    {
        if (_placements is null)
            return;

        for (int i = _placements.Count - 1; i >= 0; i--)
        {
            var placement = _placements[i];
            if (!placement.Covers(column))
                continue;

            // Printing only splits a Sixel, because only a Sixel is content. ERASING splits both:
            // a cleared cell is blank, and a picture still showing through one would be a leak
            // whichever protocol put it there.
            if (!includeOverlays && placement.Kind != Graphics.PlacementKind.Sixel)
                continue;

            _placements.RemoveAt(i);

            var before = placement.TruncatedBefore(column);
            if (before.Cols > 0)
                _placements.Insert(i, before);

            var after = placement.TruncatedAfter(column);
            if (after.Cols > 0)
                _placements.Insert(before.Cols > 0 ? i + 1 : i, after);
        }

        if (_placements.Count == 0)
            _placements = null;

        // A split can remove the last run for ONE image while runs for others remain, so this
        // cannot wait for the list to empty.
        PruneImages();
    }

    /// <summary>Splits runs across a whole written span.</summary>
    internal void SplitPlacementsOver(int column, int count, bool includeOverlays = false)
    {
        if (_placements is null)
            return;

        for (int i = 0; i < count; i++)
            SplitPlacementsAt(column + i, includeOverlays);
    }

    /// <summary>
    /// Gets the last non-whitespace cell index.
    /// </summary>
    public int GetTrimmedLength()
    {
        for (int i = _length - 1; i >= 0; i--)
        {
            if (!_cells[i].IsSpace() && !_cells[i].IsEmpty())
                return i + Math.Max(_cells[i].Width, 1);
        }
        return 0;
    }

    /// <summary>
    /// Refills this line in place, as if it had just been constructed with <paramref name="fillCell"/>.
    ///
    /// The cell array is reused rather than reallocated, which is the entire point: scrolling a full
    /// buffer discards the oldest line and builds a fresh one for the bottom, and at 240 columns that
    /// is several kilobytes of garbage per scrolled line.
    ///
    /// Cache is cleared. The renderer stores per-line formatted runs there and keys them on the line
    /// object, so a recycled line that kept its cache would draw the previous occupant's text.
    /// </summary>
    public void ResetInPlace(BufferCell fillCell, bool isWrapped = false)
    {
        Array.Fill(_cells, fillCell);
        _isWrapped = isWrapped;
        _lineAttribute = LineAttribute.Normal;

        // A recycled line keeps nothing, pictures included. The line is what holds an image alive,
        // so reusing the object without dropping its runs would keep a picture that scrolled off the
        // end of the scrollback from ever being collected — the one thing line ownership exists to
        // guarantee.
        ClearImages();

        ClearLinks();

        ClearSizedRuns();

        // And the marks. A recycled line is a NEW line -- the ring hands back the object it is about
        // to drop, so anything left on it would reappear as history that never happened, a prompt
        // marked in the middle of a program's output.
        ClearMarks();

        Cache = null;
    }

    /// <summary>
    /// Clones the line.
    /// </summary>
    public BufferLine Clone()
    {
        var newLine = new BufferLine(_length);
        newLine._isWrapped = _isWrapped;
        newLine._lineAttribute = _lineAttribute;

        // The runs are the picture, so a clone that skipped them would silently lose it.
        if (_placements is not null)
        {
            newLine._placements = new List<Graphics.LinePlacement>(_placements);
            newLine._images = _images is null ? null : new List<Graphics.TerminalImage>(_images);
        }
        for (int i = 0; i < _length; i++)
        {
            newLine._cells[i] = _cells[i];
        }
        newLine.Cache = this.Cache;
        return newLine;
    }

    /// <summary>
    /// Copies the line into another line.
    /// </summary>
    public void CopyFrom(BufferLine line)
    {
        if (_length != line._length)
        {
            _cells = new BufferCell[line._length];
            _length = line._length;
        }

        for (int i = 0; i < _length; i++)
        {
            _cells[i] = line._cells[i];
        }
        _isWrapped = line._isWrapped;
        _lineAttribute = line._lineAttribute;
        this.Cache = line.Cache;
    }

    public IEnumerator<BufferCell> GetEnumerator()
    {
        return _cells.AsEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _cells.GetEnumerator();
    }
}
