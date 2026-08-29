using System.Text;
using Wcwidth;

namespace XTerm.Common;

/// <summary>
/// Display width of a codepoint, memoised.
///
/// <c>UnicodeCalculator.GetWidth</c> measured at <b>22.9 ns per lookup</b> — it resolves a codepoint
/// by searching Unicode range tables. The print path calls it once per rune, and on non-ASCII output
/// that single call accounted for essentially the whole per-character cost: the unicode corpus ran at
/// 23.2 ns/char in total.
///
/// A direct-indexed table over the BMP answers the same question in 0.32 ns — 71x faster, for 64 KB.
/// The values are produced by the same library call, so results are identical by construction rather
/// than by a reimplementation that has to be kept in agreement with it.
///
/// Filled on demand rather than up front: populating all 65,536 entries eagerly would cost ~1.5 ms of
/// startup for a terminal that will touch a few hundred distinct codepoints. Races are benign and
/// deliberately unlocked — an <see cref="sbyte"/> write is atomic, and two threads that race on one
/// codepoint compute the same value from the same table.
///
/// Above the BMP the library is called directly. Those codepoints are mostly emoji: too sparse to
/// index, and already rare enough per cell not to matter.
/// </summary>
internal static class CellWidth
{
    /// <summary>Marks a slot that has not been computed. Real widths are -1, 0, 1 or 2.</summary>
    private const sbyte Unknown = sbyte.MinValue;

    private const int BmpEnd = 0x10000;

    private static readonly sbyte[] Bmp = CreateTable();

    private static sbyte[] CreateTable()
    {
        var table = new sbyte[BmpEnd];
        Array.Fill(table, Unknown);
        return table;
    }

    /// <summary>
    /// Width of <paramref name="codePoint"/> in cells. Equivalent to
    /// <c>UnicodeCalculator.GetWidth(new Rune(codePoint))</c>.
    /// </summary>
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

        // The two astral prepended marks (Kaithi's number sign and section mark) need the same
        // pin as the BMP ones in Compute; this path skips the memo table entirely.
        if (codePoint is 0x110BD or 0x110CD)
            return 1;

        return UnicodeCalculator.GetWidth(new Rune(codePoint));
    }

    private static sbyte Compute(int codePoint)
    {
        // Rune cannot represent an unpaired surrogate. Callers reach this through EnumerateRunes,
        // which substitutes U+FFFD, so a surrogate should never arrive here — but constructing one
        // would throw, and reporting "not printable" is the safer answer than crashing the parser.
        if (char.IsSurrogate((char)codePoint))
            return -1;

        if (IsPrependedConcatenationMark(codePoint))
            return 1;

        // Wcwidth 4.0.1's zero table runs one codepoint past the trailing jamo (U+11FF) and
        // swallows U+1200 ETHIOPIC SYLLABLE HA — a fencepost in the package's data, not a
        // property of the character. Every other Ethiopic syllable answers 1; so must this one.
        if (codePoint == 0x1200)
            return 1;

        return (sbyte)UnicodeCalculator.GetWidth(new Rune(codePoint));
    }

    /// <summary>
    /// Unicode's Prepended_Concatenation_Mark set: the Arabic number signs, end of ayah, Syriac
    /// abbreviation mark, and their kin — VISIBLE format characters that occupy a column.
    /// Pinned here because the underlying table packages disagree about them across major
    /// versions (3.0.0 says 1, 4.0.1 says 0) while python wcwidth — the referee ucs-detect
    /// measures every terminal against — says 1. Whatever Wcwidth version dependency
    /// unification resolves, these must not silently become invisible: a width-0 standalone
    /// character never moves the cursor, so the next character prints over the top of it.
    /// </summary>
    private static bool IsPrependedConcatenationMark(int codePoint) => codePoint is
        (>= 0x0600 and <= 0x0605) or 0x06DD or 0x070F or (>= 0x0890 and <= 0x0891)
        or 0x08E2 or 0x110BD or 0x110CD;
}
