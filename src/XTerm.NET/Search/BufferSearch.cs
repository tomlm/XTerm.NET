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
/// <para><b>Threading.</b> The results — <see cref="Count"/>, <see cref="CurrentIndex"/>,
/// <see cref="HitsOnRow"/>, the moves, <see cref="Find"/>, <see cref="Apply"/> and
/// <see cref="Clear"/> — belong to ONE reader thread, normally the UI's. <see cref="Find"/> also
/// reads the buffer, and the emulator is not thread-safe, so it additionally needs whatever
/// serialises writes to the terminal.</para>
///
/// <para>Output trims the buffer on its own thread — a host's PTY reader — while the renderer may be
/// drawing from a span over the hits. So nothing here listens for trims. The buffer keeps a running
/// count (<see cref="TerminalBuffer.TrimmedLines"/>), the results remember the count their rows
/// were right for, and the reader thread shifts them the next time it looks. A lock would cost every
/// row of every frame, and could not cover the span anyway, which outlives the call that returned
/// it.</para>
///
/// <para>To keep a long search off the UI thread, <see cref="BeginScan"/> one, step it with
/// <see cref="BufferScan.Next"/> on a worker, and <see cref="Apply"/> it on the reader thread.
/// <see cref="Find"/> is the same walk, done in one go.</para>
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

    /// <summary>The buffer the rows in <see cref="_hits"/> index into.</summary>
    private TerminalBuffer _hitsBuffer;

    /// <summary>
    /// The <see cref="TerminalBuffer.TrimmedLines"/> of <see cref="_hitsBuffer"/> that the rows in
    /// <see cref="_hits"/> are right for. When the buffer's count moves past it, the next read shifts
    /// the hits to catch up.
    /// </summary>
    private long _rebasedTo;

    public BufferSearch(Terminal terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _hitsBuffer = terminal.ScreenBuffer;
        _rebasedTo = _hitsBuffer.TrimmedLines;
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

    /// <summary>The term the results are for: the last one found or applied.</summary>
    public string Needle => _needle;

    internal Terminal Terminal => _terminal;

    /// <summary>
    /// Searches the whole screen buffer, replacing any previous result.
    /// </summary>
    /// <remarks>
    /// Call it on the reader thread, holding the terminal lock: it reads the buffer. A long scrollback
    /// on a busy UI thread wants <see cref="BeginScan"/> instead.
    /// </remarks>
    /// <returns>The number of runs found.</returns>
    public int Find(string needle, SearchOptions options = default)
    {
        _needle = needle ?? string.Empty;

        var buffer = _terminal.ScreenBuffer;
        var mark = buffer.TrimmedLines;
        var truncated = false;
        _hits.Clear();

        if (_needle.Length > 0)
        {
            var pattern = ToFoldedCodePoints(_needle, options.CaseSensitive);
            var matchId = 0;
            truncated = ScanRows(buffer.Lines, 0, int.MaxValue, pattern, options, _hits, ref matchId, out _);
        }

        Publish(buffer, truncated, mark);
        return _hits.Count;
    }

    /// <summary>
    /// Starts a search that runs a slice at a time, so it can be stepped on a worker thread and let go
    /// of the terminal between slices. Nothing is read and no state changes until the first
    /// <see cref="BufferScan.Next"/>; call this on any thread.
    /// </summary>
    public BufferScan BeginScan(string needle, SearchOptions options = default)
        => new(this, needle ?? string.Empty, ToFoldedCodePoints(needle ?? string.Empty, options.CaseSensitive), options);

    /// <summary>
    /// Replaces the results with a finished scan's, as <see cref="Find"/> would have left them.
    /// </summary>
    /// <remarks>
    /// <para><b>Call it on the reader thread</b> — the one that calls <see cref="Count"/>,
    /// <see cref="HitsOnRow"/> and the moves — after the last <see cref="BufferScan.Next"/> has
    /// returned. Hand the scan across the way a UI dispatcher does, so the reader sees everything the
    /// worker wrote; the two must never be inside the scan at once. No terminal lock is needed: the
    /// scan's rows are tied to a trim count on its own buffer, so lines trimmed while the scan crossed
    /// threads are applied the next time the results are read.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">The scan was begun on a different search.</exception>
    /// <exception cref="InvalidOperationException">The scan is not <see cref="BufferScanStatus.Done"/>
    /// or <see cref="BufferScanStatus.Truncated"/>.</exception>
    public void Apply(BufferScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        if (!ReferenceEquals(scan.Owner, this))
            throw new ArgumentException("The scan was begun on a different search.", nameof(scan));

        if (scan.Status is not (BufferScanStatus.Done or BufferScanStatus.Truncated))
            throw new InvalidOperationException(
                $"Only a finished scan can be applied; this one is {scan.Status}.");

        _hits.Clear();
        _hits.AddRange(scan.Hits);
        _needle = scan.Needle;
        Publish(scan.Buffer!, scan.Status == BufferScanStatus.Truncated, scan.Mark);
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
        Publish(_hitsBuffer, truncated: false, _hitsBuffer.TrimmedLines);
    }

    /// <summary>
    /// Nothing to release: trims are counted by the buffer, not by a handler here. Kept so code that
    /// disposes a search goes on compiling.
    /// </summary>
    public void Dispose()
    {
    }

    private void Publish(TerminalBuffer buffer, bool truncated, long mark)
    {
        _hitsBuffer = buffer;
        _rebasedTo = mark;
        _current = -1;
        Truncated = truncated;
    }

    // ---- the walk ------------------------------------------------------------------------

    /// <summary>
    /// Searches the logical lines that START in rows [<paramref name="startRow"/>,
    /// <paramref name="endRow"/>), following the last one past <paramref name="endRow"/> if it wraps.
    /// </summary>
    /// <param name="nextRow">The first row not searched: past the end of the last logical line, so
    /// never a continuation of one that was searched.</param>
    /// <returns>Whether <paramref name="into"/> reached <see cref="MaxHits"/>.</returns>
    internal static bool ScanRows(CircularList<BufferLine> lines, int startRow, int endRow,
                                  int[] pattern, SearchOptions options, List<SearchHit> into,
                                  ref int matchId, out int nextRow)
    {
        var end = Math.Min(endRow, lines.Length);
        var row = Math.Max(0, startRow);
        nextRow = row;

        if (into.Count >= MaxHits)
            return true;

        // Logical lines, not physical ones: a match on long output usually straddles a wrap, and
        // those are the matches worth finding. A run starts at any line not flagged IsWrapped -- and
        // at row 0 whatever its flag, because the head of that line is gone.
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
            {
                nextRow = row + 1;
                return true;
            }
        }

        nextRow = Math.Max(row, end);
        return false;
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
    /// Applies any trims that arrived since the hits were last read. One volatile read when there
    /// are none, which is every frame but the ones that follow a full scrollback scrolling.
    /// </summary>
    private void CatchUpWithTrims()
    {
        var trimmed = _hitsBuffer.TrimmedLines;
        if (trimmed != _rebasedTo)
            Rebase(trimmed);
    }

    private void Rebase(long trimmed)
    {
        var dropped = ShiftUp(_hits, trimmed - _rebasedTo);
        _rebasedTo = trimmed;

        if (_current >= 0)
            _current = Math.Max(-1, _current - dropped);
    }

    /// <summary>
    /// Shifts every hit up as the ring drops lines off the top, and drops what fell off with them.
    /// </summary>
    /// <remarks>
    /// The same arrangement <c>SelectionManager</c> uses, and for the same reason: a row index that
    /// is not adjusted goes stale silently. Nothing about a wrong row looks wrong — the highlight
    /// simply lands somewhere else while a build scrolls past. Several trims are applied as one:
    /// a row that fell off in the first is still below zero after all of them.
    /// </remarks>
    /// <returns>How many hits were dropped — always the first ones, since hits are in row order.</returns>
    internal static int ShiftUp(List<SearchHit> hits, long lines)
    {
        if (lines <= 0 || hits.Count == 0)
            return 0;

        var kept = 0;
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            var row = hit.BufferRow - lines;
            if (row < 0)
                continue;

            hits[kept++] = new SearchHit((int)row, hit.Column, hit.Cols, hit.MatchId);
        }

        var dropped = hits.Count - kept;
        hits.RemoveRange(kept, dropped);
        return dropped;
    }
}
