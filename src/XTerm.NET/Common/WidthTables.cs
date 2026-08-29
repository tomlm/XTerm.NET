namespace XTerm.Common;

/// <summary>
/// Per-codepoint display width, replicating python wcwidth's <c>wcwidth()</c> exactly — the
/// referee every terminal is measured against by ucs-detect. The range data is generated from
/// that library's own tables (see <c>scripts/generate-width-tables.py</c>), so agreement is by
/// construction; this file holds only the semantics, which are four lines of Kuhn's contract:
/// printable ASCII is 1, controls are -1, the zero table is 0, the wide table is 2, everything
/// else is 1. Vendored rather than referenced because two NuGet package versions disagreed with
/// the referee in opposite directions, and which one an APPLICATION resolved silently changed
/// what this emulator measured.
/// </summary>
internal static partial class WidthTables
{
    /// <summary>Width in cells: -1 control, else 0, 1 or 2. NUL is 0, per POSIX.</summary>
    public static int Lookup(int codePoint)
    {
        if (codePoint >= 0x20 && codePoint < 0x7F)
            return 1;

        if (codePoint == 0)
            return 0;

        if (codePoint < 0x20 || codePoint >= 0x7F && codePoint < 0xA0)
            return -1;

        if (Contains(ZeroRanges, codePoint))
            return 0;

        return Contains(WideRanges, codePoint) ? 2 : 1;
    }

    private static bool Contains(int[] ranges, int codePoint)
    {
        int lo = 0, hi = ranges.Length / 2 - 1;
        if (codePoint < ranges[0] || codePoint > ranges[ranges.Length - 1])
            return false;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (codePoint > ranges[mid * 2 + 1])
                lo = mid + 1;
            else if (codePoint < ranges[mid * 2])
                hi = mid - 1;
            else
                return true;
        }
        return false;
    }
}
