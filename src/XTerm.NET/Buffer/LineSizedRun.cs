using XTerm.Common;

namespace XTerm.Buffer;

/// <summary>
/// A span of columns on a line that was written by an OSC 66 text sizing sequence.
/// </summary>
/// <remarks>
/// <para>The same shape as <see cref="LineHyperlink"/>, and for the same reason: what the sequence
/// asked for belongs to a RUN of columns rather than to any one cell, and the cell struct is
/// deliberately 24 bytes with no reference in it. The run lives on the line, so it dies with the
/// line and costs a line that has none a single null field.</para>
///
/// <para>What the emulator guarantees is the horizontal half. The cells really are claimed — the
/// first cell of the run carries the text with <c>Width</c> equal to the columns it took, the rest
/// are zero-width continuations exactly as for a double-width character — so the cursor, selection,
/// search and reflow all agree with the client about how much room the run occupies. That is the
/// half the protocol's own capability probe measures, and the half that is useful on its own: it is
/// how a client tells the terminal a string's width instead of both sides guessing.</para>
///
/// <para>The vertical half is a renderer's. A run with <c>Scale &gt; 1</c> is drawn in a block
/// <see cref="Cols"/> wide and <c>Scale</c> cells TALL, growing downwards from this line, and
/// nothing here reserves those rows — the same arrangement DECDHL has always had, where the client
/// leaves the room. A renderer that cannot draw a scaled run should draw the run's text at the base
/// size in the first cell of the block; the columns are honestly reported either way.</para>
/// </remarks>
public readonly struct LineSizedRun
{
    /// <summary>First column the run covers.</summary>
    public readonly int Column;

    /// <summary>How many columns it covers — <c>Scale * Width</c>, or the scaled text width.</summary>
    public readonly int Cols;

    /// <summary>What the sequence asked for.</summary>
    public readonly TextSizing Sizing;

    public LineSizedRun(int column, int cols, TextSizing sizing)
    {
        Column = column;
        Cols = cols;
        Sizing = sizing;
    }

    /// <summary>One past the last column covered.</summary>
    public int EndColumn => Column + Cols;

    /// <summary>How many rows the run is drawn over, growing downwards from its line.</summary>
    public int Rows => Sizing.Scale;

    /// <summary>Whether this run covers <paramref name="column"/>.</summary>
    public bool Covers(int column) => column >= Column && column < EndColumn;

    public override string ToString() => $"{Sizing}@{Column}+{Cols}";
}
