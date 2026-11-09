namespace XTerm.Buffer;

public partial class BufferLine
{
    private long _version;

    /// <summary>
    /// A counter that moves every time this line's visible content changes: its cells, their
    /// attributes, its line attribute, or the images and sized text drawn on it.
    /// </summary>
    /// <remarks>
    /// <para>Only equality means anything. Two reads that agree saw the same content; two that
    /// differ saw at least one write between them, which may have put back what was there. It is
    /// per line and starts at zero, so it says nothing about other lines -- a scroll moves lines
    /// without writing to them. <see cref="Terminal.ContentVersion"/> and
    /// <see cref="Terminal.CopyChangedRows"/> combine it with which line sits on which row.</para>
    /// <para>Moved at the same places that clear <see cref="Cache"/>, which is the invalidation a
    /// renderer already depends on, so the two cannot disagree. Read it under the same contract as
    /// the line's cells: on the thread that writes to the terminal, or serialized with it.</para>
    /// </remarks>
    public long Version => _version;

    /// <summary>
    /// How many rows BELOW this one its sized text (OSC 66) reaches: a scale-3 run anchored here is
    /// drawn over the next two lines too, which hold nothing of it themselves.
    /// </summary>
    internal int SizedRunReach
    {
        get
        {
            var runs = _sizedRuns;
            if (runs is null)
                return 0;

            var reach = 0;
            foreach (var run in runs)
                reach = Math.Max(reach, run.Rows - 1);
            return reach;
        }
    }

    /// <summary>
    /// Every write to the line comes through here: the renderer's cache is stale, and the version
    /// moves. One increment, no branch, so it costs the print path nothing measurable.
    /// </summary>
    private void MarkChanged()
    {
        Cache = null;
        _version++;
    }
}
