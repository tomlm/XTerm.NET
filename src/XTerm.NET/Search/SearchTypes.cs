namespace XTerm.Search;

/// <summary>
/// One contiguous run of matched cells on one row.
/// </summary>
/// <remarks>
/// A match that straddles a wrap is two of these sharing a <see cref="MatchId"/> — the same shape as
/// an OSC 8 link that wrapped, and for the same reason: the thing is one match, but the screen only
/// ever draws it a row at a time.
/// </remarks>
public readonly struct SearchHit
{
    /// <summary>Absolute row in the buffer, so it stays meaningful while the viewport moves.</summary>
    public readonly int BufferRow;

    /// <summary>First column of the run.</summary>
    public readonly int Column;

    /// <summary>How many columns it covers.</summary>
    public readonly int Cols;

    /// <summary>Which match this run belongs to. Equal for the halves of a wrapped match.</summary>
    public readonly int MatchId;

    public SearchHit(int bufferRow, int column, int cols, int matchId)
    {
        BufferRow = bufferRow;
        Column = column;
        Cols = cols;
        MatchId = matchId;
    }

    /// <summary>One past the last column covered.</summary>
    public int EndColumn => Column + Cols;

    public override string ToString() => $"{BufferRow}:{Column}+{Cols}#{MatchId}";
}

/// <summary>How to match.</summary>
/// <remarks>
/// No regular expressions, deliberately. <c>Regex</c> takes a string, so supporting it would mean
/// materialising the buffer as text — which is the 16 MiB per search this whole design exists to
/// avoid, and it would be paid by every search rather than only the ones that asked for it. If it is
/// ever added it should materialise one logical line at a time, not the scrollback.
/// </remarks>
public readonly struct SearchOptions
{
    /// <summary>Match case exactly. Off by default, which is what a find box does.</summary>
    public bool CaseSensitive { get; init; }

    /// <summary>Require a non-word character either side of the match.</summary>
    public bool WholeWord { get; init; }
}
