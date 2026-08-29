using System.Text;

namespace XTerm.Common;

/// <summary>
/// Display width of a codepoint, memoised over <see cref="WidthTables.Lookup"/> — the vendored,
/// referee-exact tables (see that class for why).
///
/// A direct-indexed table over the BMP answers in 0.32 ns what a range search answers in a few
/// nanoseconds; the print path asks once per rune. The emoji plane gets the same treatment,
/// because astral lookups are not rare at all in real streams — every member of every ZWJ
/// family is one. Both tables fill on demand: populating eagerly would cost startup for a
/// terminal that touches a few hundred distinct codepoints. Races are benign and deliberately
/// unlocked — an <see cref="sbyte"/> write is atomic, and two threads that race on one
/// codepoint compute the same value from the same data.
/// </summary>
internal static class CellWidth
{
    /// <summary>Marks a slot that has not been computed. Real widths are -1, 0, 1 or 2.</summary>
    private const sbyte Unknown = sbyte.MinValue;

    private const int BmpEnd = 0x10000;
    private const int EmojiPlaneStart = 0x1F000;
    private const int EmojiPlaneEnd = 0x20000;

    private const int CjkExtStart = 0x20000;
    private const int CjkExtEnd = 0x40000;

    private static readonly sbyte[] Bmp = CreateTable(BmpEnd);
    private static readonly sbyte[] EmojiPlane = CreateTable(EmojiPlaneEnd - EmojiPlaneStart);
    private static sbyte[]? _cjkExt;

    private static sbyte[] CreateTable(int size)
    {
        var table = new sbyte[size];
        Array.Fill(table, Unknown);
        return table;
    }

    /// <summary>Width of <paramref name="codePoint"/> in cells.</summary>
    public static int Get(int codePoint)
    {
        if ((uint)codePoint < BmpEnd)
        {
            var cached = Bmp[codePoint];
            if (cached != Unknown)
                return cached;

            var computed = Compute(codePoint);
            Bmp[codePoint] = computed;
            return computed;
        }

        if ((uint)(codePoint - EmojiPlaneStart) < EmojiPlaneEnd - EmojiPlaneStart)
        {
            var slot = codePoint - EmojiPlaneStart;
            var cached = EmojiPlane[slot];
            if (cached != Unknown)
                return cached;

            var computed = (sbyte)WidthTables.Lookup(codePoint);
            EmojiPlane[slot] = computed;
            return computed;
        }

        // CJK Extension B through H: the one remaining path that is both uncached and hot in a
        // real workload — every occurrence of an extension ideograph in Chinese text lands here.
        // The table allocates on the FIRST such character, so the 128 KB costs nothing to every
        // session that never prints one.
        if ((uint)(codePoint - CjkExtStart) < CjkExtEnd - CjkExtStart)
        {
            var table = _cjkExt ??= CreateTable(CjkExtEnd - CjkExtStart);
            var slot = codePoint - CjkExtStart;
            var cached = table[slot];
            if (cached != Unknown)
                return cached;

            var computed = (sbyte)WidthTables.Lookup(codePoint);
            table[slot] = computed;
            return computed;
        }

        return WidthTables.Lookup(codePoint);
    }

    private static sbyte Compute(int codePoint)
    {
        // Rune semantics: an unpaired surrogate is not printable. Callers reach this through
        // EnumerateRunes, which substitutes U+FFFD, so a surrogate should never arrive here —
        // but "not printable" is the safer answer than pretending it has a width.
        if (char.IsSurrogate((char)codePoint))
            return -1;

        return (sbyte)WidthTables.Lookup(codePoint);
    }
}
