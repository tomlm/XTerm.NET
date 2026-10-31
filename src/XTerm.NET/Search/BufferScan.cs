using XTerm.Buffer;

namespace XTerm.Search;

/// <summary>What <see cref="BufferScan.Next"/> found, and what the caller does next.</summary>
public enum BufferScanStatus
{
    /// <summary>More of the buffer to search: call <see cref="BufferScan.Next"/> again.</summary>
    More,

    /// <summary>The whole buffer is searched: <see cref="BufferSearch.Apply"/> the scan.</summary>
    Done,

    /// <summary>
    /// <see cref="BufferSearch.MaxHits"/> was reached and the rest was not searched. Finished like
    /// <see cref="Done"/>; applying it sets <see cref="BufferSearch.Truncated"/>.
    /// </summary>
    Truncated,

    /// <summary>
    /// The rows found so far no longer mean what they did: the terminal switched screens, or the
    /// buffer was resized (a width change reflows every line). The hits are discarded. Throw the scan
    /// away and <see cref="BufferSearch.BeginScan"/> again.
    /// </summary>
    Invalidated,
}

/// <summary>
/// One search over the buffer, run a slice at a time so the terminal can be let go between slices.
/// Made by <see cref="BufferSearch.BeginScan"/>, stepped with <see cref="Next"/>, published with
/// <see cref="BufferSearch.Apply"/>.
/// </summary>
/// <remarks>
/// <para>The scan owns its hits, its position and the trim count both are right for. Every
/// <see cref="Next"/> first shifts all of them for the lines the buffer trimmed since the last one,
/// then searches on. So the hits from every slice are always in ONE frame — the buffer as it stood
/// when the latest slice ran — and the next slice resumes after the last whole logical line it
/// searched, never part way into one.</para>
///
/// <para><b>Threading.</b> <see cref="Next"/> runs on any thread holding the terminal lock — the
/// lock that serialises writes to the terminal — one call at a time; it need not be the reader
/// thread. <see cref="BufferSearch.Apply"/> runs on the search's reader thread once the last
/// <see cref="Next"/> has returned. The properties are for whichever of the two currently holds the
/// scan.</para>
/// </remarks>
public sealed class BufferScan
{
    private readonly int[] _pattern;
    private readonly List<SearchHit> _hits = new();

    private TerminalBuffer? _buffer;
    private long _mark;
    private int _resizes;
    private int _cursor;
    private int _nextMatchId;

    internal BufferScan(BufferSearch owner, string needle, int[] pattern, SearchOptions options)
    {
        Owner = owner;
        Needle = needle;
        Options = options;
        _pattern = pattern;
    }

    /// <summary>The term being searched for.</summary>
    public string Needle { get; }

    /// <summary>How it is being matched.</summary>
    public SearchOptions Options { get; }

    /// <summary>Where the scan stands. Starts at <see cref="BufferScanStatus.More"/>.</summary>
    public BufferScanStatus Status { get; private set; } = BufferScanStatus.More;

    /// <summary>How many runs have been found so far, after dropping any that scrolled away.</summary>
    public int Count => _hits.Count;

    internal BufferSearch Owner { get; }
    internal TerminalBuffer? Buffer => _buffer;
    internal long Mark => _mark;
    internal List<SearchHit> Hits => _hits;

    /// <summary>
    /// Searches up to <paramref name="maxRows"/> more rows. Call it holding the terminal lock.
    /// </summary>
    /// <param name="maxRows">How many rows this slice may START logical lines on. A line that wraps
    /// past the slice is followed to its end, so a slice can read a little more.</param>
    /// <returns>
    /// <see cref="BufferScanStatus.More"/> to call again; <see cref="BufferScanStatus.Done"/> or
    /// <see cref="BufferScanStatus.Truncated"/> to <see cref="BufferSearch.Apply"/> it;
    /// <see cref="BufferScanStatus.Invalidated"/> to begin again. Once finished or invalidated, it
    /// keeps returning that status without reading anything.
    /// </returns>
    public BufferScanStatus Next(int maxRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);

        if (Status != BufferScanStatus.More)
            return Status;

        var buffer = Owner.Terminal.ScreenBuffer;

        if (_buffer is null)
        {
            // The first slice is where the scan is tied to a buffer and a moment in its life --
            // here, under the lock, rather than in BeginScan, which may run anywhere.
            _buffer = buffer;
            _mark = buffer.TrimmedLines;
            _resizes = buffer.ResizeCount;

            if (_pattern.Length == 0)
                return Status = BufferScanStatus.Done;
        }
        else if (!ReferenceEquals(buffer, _buffer) || buffer.ResizeCount != _resizes)
        {
            _hits.Clear();
            return Status = BufferScanStatus.Invalidated;
        }
        else
        {
            CatchUpWithTrims(buffer);
        }

        var end = (int)Math.Min((long)_cursor + maxRows, int.MaxValue);
        if (BufferSearch.ScanRows(buffer.Lines, _cursor, end, _pattern, Options, _hits,
                                  ref _nextMatchId, out _cursor))
            return Status = BufferScanStatus.Truncated;

        return Status = _cursor >= buffer.Lines.Length ? BufferScanStatus.Done : BufferScanStatus.More;
    }

    /// <summary>
    /// Moves the hits AND the position up by the lines trimmed since the last slice.
    /// </summary>
    /// <remarks>
    /// Shifting only one of them is the mistake this class exists to prevent: hits left in the old
    /// frame draw that many rows low beside the new slice's, and a large trim puts a new hit above
    /// an old one. The position sits just past a whole logical line, so after the shift it still
    /// does — unless the trim took everything up to it and beyond, in which case it clamps to 0,
    /// where the head of whatever line is left has gone and it starts a run, as it does for Find.
    /// </remarks>
    private void CatchUpWithTrims(TerminalBuffer buffer)
    {
        var trimmed = buffer.TrimmedLines;
        var lines = trimmed - _mark;
        if (lines == 0)
            return;

        _mark = trimmed;
        BufferSearch.ShiftUp(_hits, lines);
        _cursor = (int)Math.Max(0, _cursor - lines);
    }
}
