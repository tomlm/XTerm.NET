using XTerm.Buffer;

namespace XTerm.Search;

/// <summary>
/// Finds text in the scrollback, without turning the scrollback into text.
/// </summary>
/// <remarks>
/// <para>The obvious implementation asks each line for a string and runs <c>IndexOf</c> over it.
/// Measured on a 10,000-line buffer at 240 columns, that costs 9.7 ms and <b>16.3 MiB</b> per search;
/// reading codepoints straight out of the cells costs 3.7 ms and allocates nothing, for the same
/// 25,325 hits. A find box searches on every keystroke, so the first of those is roughly 80 MiB to
/// type "error" — in a library that went to some trouble to stop allocating per character.</para>
///
/// <para>So nothing here builds a string. The walk moves a (row, column) cursor through the buffer
/// and compares codepoints, which also removes the step where a character offset has to be mapped
/// back onto cells: the position IS the cursor, and a match that crosses a wrap simply produces two
/// runs because the cursor changed row part way through.</para>
///
/// <para><b>Threading.</b> <see cref="Find"/> and <see cref="Scan"/> read the buffer directly and the
/// emulator is not thread-safe, so call them under whatever serialises writes to the terminal. The
/// results — <see cref="Count"/>, <see cref="HitsOnRow"/>, the moves, <see cref="Find"/>,
/// <see cref="Apply(IEnumerable{SearchHit}, bool, long)"/> and <see cref="Clear"/> — belong to ONE
/// reader thread, normally the UI's. <see cref="TerminalBuffer.Trimmed"/> may arrive on any thread at
/// any time: a host raises it from its output thread, while the renderer is drawing from a span over
/// the hits. So a trim only counts the lines dropped, and the reader thread shifts the results the
/// next time it looks at them. A lock would cost every row of every frame, and could not cover the
/// span anyway, which outlives the call that returned it.</para>
///
/// <para>A search can be split to keep it off the UI thread: <see cref="Scan"/> slices of the buffer
/// on a worker, then hand the hits to <see cref="Apply(IEnumerable{SearchHit}, bool, long)"/> on the
/// reader thread. <see cref="Find"/> is exactly that, done in one go.</para>
/// </remarks>
public sealed class BufferSearch : IDisposable
{
    /// <summary>
    /// The most matches kept.
    /// </summary>
    /// <remarks>
    /// A single-letter search over a long scrollback matches almost everything, and every match kept
    /// is a struct in a list. The cap bounds that — and <see cref="Truncated"/> says when it bit,
    /// because a count that quietly stops being true reads as a bug in the search rather than a
    /// limit on it.
    /// </remarks>
    public const int MaxHits = 10_000;

    private readonly Terminal _terminal;
    private readonly List<SearchHit> _hits = new();

    private string _needle = string.Empty;
    private int _current = -1;
    private bool _disposed;

    /// <summary>
    /// Lines the buffer has dropped off the top since this search was made. Written by
    /// <see cref="OnTrimmed"/> on whatever thread raises it, and nothing else.
    /// </summary>
    private long _trimmed;

    /// <summary>
    /// The value of <see cref="_trimmed"/> that the rows in <see cref="_hits"/> are correct for.
    /// Reader thread only; when it falls behind, the next read shifts the hits to catch up.
    /// </summary>
    private long _rebasedTo;

    public BufferSearch(Terminal terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _terminal.Buffer.Trimmed += OnTrimmed;
    }

    /// <summary>How many runs are held. See <see cref="MaxHits"/>.</summary>
    public int Count
    {
        get
        {
            CatchUpWithTrims();
            return _hits.Count;
        }
    }

    /// <summary>Whether the cap was reached and matches beyond it were not kept.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Index of the current match within <see cref="Count"/>, or -1 before one is chosen.</summary>
    public int CurrentIndex
    {
        get
        {
            CatchUpWithTrims();
            return _current;
        }
    }

    /// <summary>
    /// The term last given to <see cref="Find"/>. <see cref="Apply(IEnumerable{SearchHit}, bool, long)"/>
    /// is not told the needle and leaves this as it was.
    /// </summary>
    public string Needle => _needle;

    /// <summary>
    /// How many lines the buffer has trimmed off the top since this search was made. Safe to read
    /// from any thread.
    /// </summary>
    /// <remarks>
    /// The mark that ties hits from <see cref="Scan"/> to a moment in the buffer's life. Read it at
    /// the point the scanned rows are correct — under the terminal lock, when the last slice ends —
    /// and pass it to <see cref="Apply(IEnumerable{SearchHit}, bool, long)"/>, which shifts the hits
    /// by whatever was trimmed between then and now.
    /// </remarks>
    public long TrimMark => Volatile.Read(ref _trimmed);

    /// <summary>
    /// Searches the whole buffer, replacing any previous result.
    /// </summary>
    /// <remarks>
    /// <see cref="Scan"/> over every row and then <see cref="Apply(IEnumerable{SearchHit}, bool, long)"/>,
    /// except that the scan writes straight into the result list rather than into one that is then
    /// copied. Reads the buffer, so call it on the reader thread while holding the terminal lock.
    /// </remarks>
    /// <returns>The number of runs found.</returns>
    public int Find(string needle, SearchOptions options = default)
    {
        _needle = needle ?? string.Empty;

        var mark = TrimMark;
        var truncated = false;
        _hits.Clear();

        if (_needle.Length > 0)
        {
            var pattern = ToFoldedCodePoints(_needle, options.CaseSensitive);
            truncated = ScanCore(_terminal.Buffer.Lines, 0, int.MaxValue, pattern, options, _hits).Truncated;
        }

        Publish(truncated, mark);
        return _hits.Count;
    }

    /// <summary>
    /// Searches the logical lines that START in rows [<paramref name="startRow"/>,
    /// <paramref name="endRow"/>), appending what it finds to <paramref name="into"/>. Touches no
    /// search's state, so it can run on any thread that holds the terminal lock.
    /// </summary>
    /// <param name="buffer">The buffer to read. Pass the same one for every slice of a search; if the
    /// terminal switches screens part way, start again.</param>
    /// <param name="startRow">First row, as an absolute index into <see cref="TerminalBuffer.Lines"/> —
    /// the same coordinate as <see cref="SearchHit.BufferRow"/>, not relative to the viewport. Use 0,
    /// or the previous slice's <see cref="SearchScan.NextRow"/>. A continuation row here is skipped:
    /// it belongs to a run that starts above, which the slice before searched in full.</param>
    /// <param name="endRow">One past the last row whose line may start a run. A run that starts
    /// before it is followed to its end, past this row if it wraps.</param>
    /// <param name="into">Where hits are appended, in row order. Match ids continue from the last hit
    /// already in it, so appending slice after slice to one list gives the same hits as one scan of
    /// the lot. <see cref="MaxHits"/> caps the list as a whole.</param>
    /// <remarks>
    /// Rows outside the buffer are clamped rather than refused: a slice boundary that has been shifted
    /// for trims can fall off the top.
    /// </remarks>
    public static SearchScan Scan(TerminalBuffer buffer, int startRow, int endRow,
                                  string needle, SearchOptions options, List<SearchHit> into)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(into);

        if (string.IsNullOrEmpty(needle))
            return new SearchScan(Math.Clamp(endRow, 0, buffer.Lines.Length), truncated: false);

        return ScanCore(buffer.Lines, startRow, endRow, ToFoldedCodePoints(needle, options.CaseSensitive),
                        options, into);
    }

    /// <summary>
    /// Publishes hits whose rows are correct for the buffer as it stands now.
    /// </summary>
    /// <remarks>
    /// Only right when no trim can land between the rows being correct and this call — that is,
    /// while holding the terminal lock. Otherwise use the overload that takes a
    /// <see cref="TrimMark"/>.
    /// </remarks>
    public void Apply(IEnumerable<SearchHit> hits, bool truncated) => Apply(hits, truncated, TrimMark);

    /// <summary>
    /// Replaces the results with <paramref name="hits"/>, as <see cref="Find"/> would have left them.
    /// Call it on the reader thread.
    /// </summary>
    /// <param name="hits">Runs in row order with match ids that never decrease — what
    /// <see cref="Scan"/> produces. Copied, so the caller may reuse the list.</param>
    /// <param name="truncated">Whether the scan hit <see cref="MaxHits"/>; becomes
    /// <see cref="Truncated"/>.</param>
    /// <param name="trimMark">The <see cref="TrimMark"/> at which the hits' rows were correct. Lines
    /// trimmed since are applied before the hits are next read, and hits that went with them are
    /// dropped — so a trim racing this call, or the trip to the UI thread before it, costs nothing.</param>
    public void Apply(IEnumerable<SearchHit> hits, bool truncated, long trimMark)
    {
        ArgumentNullException.ThrowIfNull(hits);

        if (trimMark < 0 || trimMark > TrimMark)
            throw new ArgumentOutOfRangeException(nameof(trimMark), trimMark,
                "A trim mark is a value TrimMark has already returned.");

        _hits.Clear();
        _hits.AddRange(hits);

        // HitsOnRow binary-searches by row and the moves step by id; out of order, both go quietly
        // wrong. Checking costs one pass over at most MaxHits structs, once per search.
        for (var i = 1; i < _hits.Count; i++)
        {
            if (_hits[i].BufferRow < _hits[i - 1].BufferRow || _hits[i].MatchId < _hits[i - 1].MatchId)
            {
                var message = $"Hits must be in row order with non-decreasing match ids; {_hits[i]} follows {_hits[i - 1]}.";
                _hits.Clear();
                Publish(truncated: false, TrimMark);
                throw new ArgumentException(message, nameof(hits));
            }
        }

        Publish(truncated, trimMark);
    }

    /// <summary>
    /// The runs on one row, or empty for the rows that have none — which is nearly all of them.
    /// </summary>
    /// <remarks>
    /// A span rather than a list because this is asked once per row per frame, and the answer is
    /// usually nothing. Hits are produced in row order, so the row's block is found by binary search
    /// and handed back in place. The span stays put until the next call into this search on the
    /// reader thread; a trim on another thread does not move it.
    /// </remarks>
    public ReadOnlySpan<SearchHit> HitsOnRow(int bufferRow)
    {
        CatchUpWithTrims();

        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_hits);

        var lo = 0;
        var hi = all.Length - 1;
        var found = -1;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (all[mid].BufferRow < bufferRow) lo = mid + 1;
            else if (all[mid].BufferRow > bufferRow) hi = mid - 1;
            else { found = mid; hi = mid - 1; }
        }

        if (found < 0)
            return ReadOnlySpan<SearchHit>.Empty;

        var length = 0;
        while (found + length < all.Length && all[found + length].BufferRow == bufferRow)
            length++;

        return all.Slice(found, length);
    }

    /// <summary>Steps to the next match, wrapping round at the end.</summary>
    public bool TryMoveNext(out SearchHit hit) => TryMove(1, out hit);

    /// <summary>Steps to the previous match, wrapping round at the start.</summary>
    public bool TryMovePrevious(out SearchHit hit) => TryMove(-1, out hit);

    /// <summary>Forgets the results, keeping the search usable.</summary>
    public void Clear()
    {
        _hits.Clear();
        _needle = string.Empty;
        Publish(truncated: false, TrimMark);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _terminal.Buffer.Trimmed -= OnTrimmed;
    }

    private void Publish(bool truncated, long trimMark)
    {
        _current = -1;
        Truncated = truncated;
        _rebasedTo = trimMark;
    }

    // ---- the walk ------------------------------------------------------------------------

    private static SearchScan ScanCore(CircularList<BufferLine> lines, int startRow, int endRow,
                                       int[] pattern, SearchOptions options, List<SearchHit> into)
    {
        var end = Math.Min(endRow, lines.Length);
        var row = Math.Max(0, startRow);

        if (into.Count >= MaxHits)
            return new SearchScan(row, truncated: true);

        var matchId = into.Count > 0 ? into[^1].MatchId + 1 : 0;

        // Logical lines, not physical ones: a match on long output usually straddles a wrap, and
        // those are the matches worth finding. A run starts at any line not flagged IsWrapped.
        for (; row < end; row++)
        {
            if (lines[row] is null)
                continue;

            if (row > 0 && lines[row]!.IsWrapped)
                continue;   // a continuation; it was searched as part of the run above

            var runEnd = row;
            while (runEnd + 1 < lines.Length && lines[runEnd + 1] is { IsWrapped: true })
                runEnd++;

            var truncated = SearchRun(lines, row, runEnd, pattern, options, into, ref matchId);
            row = runEnd;

            if (truncated)
                return new SearchScan(row + 1, truncated: true);
        }

        return new SearchScan(Math.Max(row, end), truncated: false);
    }

    /// <returns>Whether the cap was reached.</returns>
    private static bool SearchRun(CircularList<BufferLine> lines, int startRow, int endRow,
                                  int[] pattern, SearchOptions options, List<SearchHit> into, ref int matchId)
    {
        for (var row = startRow; row <= endRow; row++)
        {
            var line = lines[row];
            if (line is null)
                continue;

            for (var col = 0; col < line.Length; col++)
            {
                if (!Matches(lines, endRow, row, col, pattern, options.CaseSensitive, out var afterRow, out var afterCol))
                    continue;

                if (options.WholeWord && !IsWholeWord(lines, startRow, endRow, row, col, afterRow, afterCol))
                    continue;

                AddRuns(lines, into, matchId++, row, col, afterRow, afterCol);

                if (into.Count >= MaxHits)
                    return true;

                // Matches do not overlap. Step past this one, which may have ended on a later row.
                if (afterRow != row)
                {
                    row = afterRow;
                    line = lines[row];
                    if (line is null)
                        return false;
                    col = afterCol - 1;
                }
                else
                {
                    col = afterCol - 1;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the needle sits at (row, col), and where it ends if so.
    /// </summary>
    /// <remarks>
    /// Width-0 cells are stepped over rather than compared. There are two kinds and neither is a
    /// character of its own: the placeholder behind a wide glyph, and a combining mark that found
    /// nothing to attach to. Comparing either would make a needle fail to match text that reads
    /// exactly like it.
    /// </remarks>
    private static bool Matches(CircularList<BufferLine> lines, int endRow, int row, int col,
                                int[] pattern, bool caseSensitive, out int afterRow, out int afterCol)
    {
        afterRow = row;
        afterCol = col;

        var r = row;
        var c = col;

        for (var i = 0; i < pattern.Length; i++)
        {
            if (!Advance(lines, endRow, ref r, ref c, skipZeroWidth: i > 0))
                return false;

            var cell = lines[r]![c];
            if (cell.Width == 0)
                return false;

            if (Fold(cell.CodePoint, caseSensitive) != pattern[i])
                return false;

            afterRow = r;
            afterCol = c + 1;
            c++;
        }

        return true;
    }

    /// <summary>
    /// Puts the cursor on the next cell that carries a character, crossing a wrap if it has to.
    /// </summary>
    private static bool Advance(CircularList<BufferLine> lines, int endRow, ref int row, ref int col,
                                bool skipZeroWidth)
    {
        while (true)
        {
            var line = lines[row];
            if (line is null)
                return false;

            if (col >= line.Length)
            {
                if (row >= endRow)
                    return false;

                row++;
                col = 0;
                continue;
            }

            if (skipZeroWidth && lines[row]![col].Width == 0)
            {
                col++;
                continue;
            }

            return true;
        }
    }

    /// <summary>
    /// The needle as CODEPOINTS, folded for case. A cell stores a codepoint, and comparing it to
    /// UTF-16 chars one at a time can never match anything outside the BMP -- an emoji in the
    /// needle is two chars and neither equals the cell. Built once per search.
    /// </summary>
    /// <remarks>
    /// A cell whose text is a multi-codepoint cluster is compared by its LEADING codepoint, which
    /// is all the cell stores inline. Matching the full cluster text would mean materialising it
    /// per cell -- the same cost this class exists to avoid, and the same reason regular
    /// expressions are not offered. The limitation is one-directional: base-character searches
    /// still find cells that carry combining marks.
    /// </remarks>
    private static int[] ToFoldedCodePoints(string needle, bool caseSensitive)
    {
        var points = new List<int>(needle.Length);
        for (var i = 0; i < needle.Length; i++)
        {
            int cp = needle[i];
            if (char.IsHighSurrogate(needle[i]) && i + 1 < needle.Length && char.IsLowSurrogate(needle[i + 1]))
            {
                cp = char.ConvertToUtf32(needle[i], needle[i + 1]);
                i++;
            }
            points.Add(Fold(cp, caseSensitive));
        }
        return points.ToArray();
    }

    private static int Fold(int codePoint, bool caseSensitive)
    {
        if (caseSensitive || codePoint > 0xFFFF)
            return codePoint;   // astral case pairs are rare enough that exact match is honest

        return char.ToUpperInvariant((char)codePoint);
    }

    /// <summary>
    /// Splits a match into one run per row and records them under one id.
    /// </summary>
    private static void AddRuns(CircularList<BufferLine> lines, List<SearchHit> into, int matchId,
                                int startRow, int startCol, int endRow, int endCol)
    {
        for (var row = startRow; row <= endRow; row++)
        {
            var from = row == startRow ? startCol : 0;
            var to = row == endRow ? endCol : (lines[row]?.Length ?? from);

            if (to > from)
                into.Add(new SearchHit(row, from, to - from, matchId));
        }
    }

    private static bool IsWholeWord(CircularList<BufferLine> lines, int startRow, int endRow,
                             int row, int col, int afterRow, int afterCol)
    {
        return !IsWordCharacterBefore(lines, startRow, row, col)
            && !IsWordCharacterAt(lines, endRow, afterRow, afterCol);
    }

    private static bool IsWordCharacterBefore(CircularList<BufferLine> lines, int startRow, int row, int col)
    {
        var r = row;
        var c = col - 1;

        while (true)
        {
            while (c < 0)
            {
                if (r <= startRow)
                    return false;   // start of the logical line; nothing before it

                r--;
                c = (lines[r]?.Length ?? 0) - 1;
            }

            // A width-0 cell is not a character -- it is the placeholder behind a wide glyph, and
            // treating it as the neighbour would let a whole-word match sit flush against a CJK
            // letter. Step past it to the glyph that owns it.
            if ((lines[r]?[c].Width ?? 1) == 0)
            {
                c--;
                continue;
            }

            return IsWordCharacter(lines[r]?[c].CodePoint ?? 0);
        }
    }

    private static bool IsWordCharacterAt(CircularList<BufferLine> lines, int endRow, int row, int col)
    {
        var r = row;
        var c = col;

        if (!Advance(lines, endRow, ref r, ref c, skipZeroWidth: false))
            return false;   // end of the logical line

        return IsWordCharacter(lines[r]?[c].CodePoint ?? 0);
    }

    private static bool IsWordCharacter(int codePoint)
        => codePoint == '_'
           || (codePoint <= 0xFFFF && char.IsLetterOrDigit((char)codePoint));

    private bool TryMove(int direction, out SearchHit hit)
    {
        CatchUpWithTrims();

        hit = default;
        if (_hits.Count == 0)
            return false;

        // Step by MATCH rather than by run, so a match that wrapped is one stop rather than two.
        var startId = _current >= 0 ? _hits[_current].MatchId : (direction > 0 ? -1 : int.MaxValue);
        var best = -1;

        for (var i = 0; i < _hits.Count; i++)
        {
            var id = _hits[i].MatchId;
            if (direction > 0 ? id > startId : id < startId)
            {
                best = i;
                if (direction > 0)
                    break;
            }
        }

        if (best < 0)
            best = direction > 0 ? 0 : _hits.Count - 1;   // wrap round

        _current = best;
        hit = _hits[best];
        return true;
    }

    /// <summary>
    /// Counts the lines the ring drops off the top, and does nothing else.
    /// </summary>
    /// <remarks>
    /// This runs on whichever thread wrote the output — a host's PTY reader, while the UI thread may
    /// be holding a span over the hits — so it must not touch them. The reader thread shifts them
    /// in <see cref="CatchUpWithTrims"/>.
    /// </remarks>
    private void OnTrimmed(int count)
    {
        if (count > 0)
            Interlocked.Add(ref _trimmed, count);
    }

    /// <summary>
    /// Applies any trims that arrived since the hits were last read. One volatile read when there
    /// are none, which is every frame but the ones that follow a full scrollback scrolling.
    /// </summary>
    private void CatchUpWithTrims()
    {
        var trimmed = Volatile.Read(ref _trimmed);
        if (trimmed != _rebasedTo)
            Rebase(trimmed);
    }

    /// <summary>
    /// Shifts every result up as the ring drops lines off the top, and drops what fell off with them.
    /// </summary>
    /// <remarks>
    /// The same arrangement <c>SelectionManager</c> uses, and for the same reason: a row index that
    /// is not adjusted goes stale silently. Nothing about a wrong row looks wrong — the highlight
    /// simply lands somewhere else while a build scrolls past. Several trims are applied as one:
    /// a row that fell off in the first is still below zero after all of them.
    /// </remarks>
    private void Rebase(long trimmed)
    {
        var count = trimmed - _rebasedTo;
        _rebasedTo = trimmed;

        if (_hits.Count == 0)
            return;

        var kept = 0;
        for (var i = 0; i < _hits.Count; i++)
        {
            var hit = _hits[i];
            var row = hit.BufferRow - count;
            if (row < 0)
                continue;

            _hits[kept++] = new SearchHit((int)row, hit.Column, hit.Cols, hit.MatchId);
        }

        var dropped = _hits.Count - kept;
        _hits.RemoveRange(kept, dropped);

        if (_current >= 0)
            _current = Math.Max(-1, _current - dropped);
    }
}
