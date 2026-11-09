using XTerm.Buffer;

namespace XTerm;

/// <summary>
/// Which visible rows changed, for a host that has no renderer of its own to tell it.
/// </summary>
/// <remarks>
/// <para><b>What counts as a change.</b> The content of a visible row: its cells and their
/// attributes, its line attribute, the images and sized text on it -- and which buffer line the
/// row shows at all. That last part is what makes a scroll, a resize, a viewport scroll and the
/// alternate-screen switch count without any of them writing to the lines they move: the row
/// shows a different line, so it changed. A write that puts back what was there still counts;
/// nothing compares cell contents.</para>
/// <para><b>What does not.</b> The cursor: moving it changes no row, and a host reads
/// <see cref="TerminalBuffer.X"/>/<see cref="TerminalBuffer.Y"/> and
/// <see cref="CursorVisible"/> directly. Nor screen-wide presentation state -- the palette,
/// <see cref="ReverseVideo"/>, the title -- which belongs to no row.</para>
/// <para><b>How.</b> A write costs one increment on the line it touches
/// (<see cref="BufferLine.Version"/>), and nothing else on the print path. The comparison happens
/// when a host asks -- <see cref="RefreshContentVersion"/>, <see cref="CopyChangedRows"/> and
/// <see cref="GetRowVersion"/> each look at every visible row once, against what the previous
/// look saw, and stamp the rows that differ with a new <see cref="ContentVersion"/>. Asking is
/// O(rows), never O(cells). <see cref="ContentVersion"/> itself only reads the result. Because the stamps live here rather than with the caller, any number
/// of observers can each keep their own last-seen version.</para>
/// <para><b>Sized text.</b> An OSC 66 run is stored on its anchor line alone but drawn over the
/// rows below it, so when a line changes, every visible row its sized text covered before or
/// covers now is stamped with it -- including from an anchor above the top of the viewport.</para>
/// <para><b>Threads.</b> The same contract as <see cref="Write(string)"/>: call these on the thread
/// that writes to the terminal, or serialized with it. The three methods read the buffer and
/// update the stamps, so they are not safe to call concurrently with a write or with each other.
/// <see cref="ContentVersion"/> reads one field and writes nothing, so it can be read from any
/// thread, without a lock -- what it reports is the last look, not the buffer as it is now.</para>
/// </remarks>
public partial class Terminal
{
    private long _contentVersion;
    private BufferLine?[] _seenLines = [];
    private long[] _seenLineVersions = [];
    private int[] _seenReach = [];
    private long[] _rowVersions = [];

    /// <summary>
    /// Lines above the viewport that are watched too: the most a sized run anchored above it can
    /// reach into it.
    /// </summary>
    private const int AboveViewport = Common.TextSizing.MaxScale - 1;

    /// <summary>
    /// The version as of the last look: the value <see cref="RefreshContentVersion"/>,
    /// <see cref="CopyChangedRows"/> or <see cref="GetRowVersion"/> last brought up to date.
    /// </summary>
    /// <remarks>
    /// <para>A pure read: it does not look at the buffer, so a write since the last look is not in
    /// it yet. To ask "has anything changed?", call <see cref="RefreshContentVersion"/>. Zero
    /// before the first look, which is also the value that reports every row as changed.</para>
    /// <para>The usual loop needs no refresh of its own: pass the value from last round to
    /// <see cref="CopyChangedRows"/>, which looks first, then read this for the next round.</para>
    /// </remarks>
    public long ContentVersion => Volatile.Read(ref _contentVersion);

    /// <summary>
    /// Looks at the visible rows, stamps the ones that changed since the last look, and returns
    /// the up-to-date <see cref="ContentVersion"/>.
    /// </summary>
    /// <remarks>
    /// It moves once per look that found a change, however many rows or writes that change
    /// spanned, and not at all when nothing did. Compare it for equality, or pass an earlier value
    /// to <see cref="CopyChangedRows"/> to learn which rows moved. Same threading contract as
    /// <see cref="Write(string)"/>; see the remarks on this class.
    /// </remarks>
    public long RefreshContentVersion()
    {
        SyncRowVersions();
        return _contentVersion;
    }

    /// <summary>
    /// The <see cref="ContentVersion"/> at which visible row <paramref name="row"/> (0 is the top
    /// of the viewport) last changed. Looks at the visible rows first, as
    /// <see cref="RefreshContentVersion"/> does.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The row is outside the viewport.</exception>
    public long GetRowVersion(int row)
    {
        SyncRowVersions();
        if ((uint)row >= (uint)_rowVersions.Length)
            throw new ArgumentOutOfRangeException(nameof(row), row, "Not a visible row.");
        return _rowVersions[row];
    }

    /// <summary>
    /// Marks, in <paramref name="changed"/>, every visible row that changed after
    /// <paramref name="sinceVersion"/>, and returns how many did.
    /// </summary>
    /// <remarks>
    /// <para>Index 0 is the top of the viewport. Rows past the span's end are not reported, and
    /// entries past <see cref="Rows"/> are set false. Pass 0 to get every row.</para>
    /// <para>Looks at the visible rows first, as <see cref="RefreshContentVersion"/> does. The
    /// usual loop: pass last round's version here (0 the first time), render or snapshot the
    /// marked rows, then read <see cref="ContentVersion"/> -- now current -- for the next round.
    /// Nothing allocates.</para>
    /// </remarks>
    public int CopyChangedRows(long sinceVersion, Span<bool> changed)
    {
        SyncRowVersions();

        var rows = _rowVersions;
        var count = 0;
        for (int y = 0; y < changed.Length; y++)
        {
            var isChanged = y < rows.Length && rows[y] > sinceVersion;
            changed[y] = isChanged;
            if (isChanged)
                count++;
        }

        return count;
    }

    private void SyncRowVersions()
    {
        var buffer = _buffer;
        var rows = Rows;

        // Watched lines: AboveViewport of them above the top row, then every visible row. Index i
        // is the line at ViewportY - AboveViewport + i, so visible row y is index y + AboveViewport.
        var first = buffer.ViewportY - AboveViewport;
        var watched = rows + AboveViewport;

        // A different row count -- a resize, or the first look -- changes every row. The old
        // stamps describe a grid that is not there any more. Keyed on the WATCHED count, which
        // is never zero: a terminal built with zero rows (before layout) has a zero-length
        // _rowVersions on its first look too, and must still get its watched arrays.
        if (_seenLines.Length != watched)
        {
            _seenLines = new BufferLine?[watched];
            _seenLineVersions = new long[watched];
            _seenReach = new int[watched];
            _rowVersions = new long[rows];
            var all = Interlocked.Increment(ref _contentVersion);
            for (int i = 0; i < watched; i++)
            {
                var line = buffer.GetLine(first + i);
                _seenLines[i] = line;
                _seenLineVersions[i] = line?.Version ?? 0;
                _seenReach[i] = line?.SizedRunReach ?? 0;
            }
            Array.Fill(_rowVersions, all);
            return;
        }

        long stamp = 0;
        for (int i = 0; i < watched; i++)
        {
            var line = buffer.GetLine(first + i);
            var version = line?.Version ?? 0;
            if (ReferenceEquals(line, _seenLines[i]) && version == _seenLineVersions[i])
                continue;

            var reach = line?.SizedRunReach ?? 0;
            var covered = Math.Max(reach, _seenReach[i]);
            _seenLines[i] = line;
            _seenLineVersions[i] = version;
            _seenReach[i] = reach;

            // The row itself, and the rows its sized text covered before or covers now. A line
            // above the viewport with nothing reaching into it stamps nothing, and moves nothing.
            var from = Math.Max(0, i - AboveViewport);
            var to = Math.Min(rows - 1, i - AboveViewport + covered);
            if (from > to)
                continue;

            if (stamp == 0)
                stamp = Interlocked.Increment(ref _contentVersion);

            for (int y = from; y <= to; y++)
                _rowVersions[y] = stamp;
        }
    }
}
