namespace XTerm.Common;

/// <summary>
/// Character set definitions for VT100/xterm terminals.
/// Provides translations for box drawing and special character sets.
/// All escape sequences use \u0000 format to avoid hex parsing ambiguities.
/// </summary>
public static class Charsets
{
    /// <summary>
    /// VT100 Line Drawing character set (DEC Special Graphics).
    /// Maps ASCII characters to box drawing and special symbols.
    /// </summary>
    public static readonly Dictionary<char, string> VT100LineDrawing = new()
    {
        // Box Drawing Characters
        { 'j', "\u2518" }, // ┘ Bottom right corner
        { 'k', "\u2510" }, // ┐ Top right corner
        { 'l', "\u250c" }, // ┌ Top left corner
        { 'm', "\u2514" }, // └ Bottom left corner
        { 'n', "\u253c" }, // ┼ Cross/intersection
        { 'q', "\u2500" }, // ─ Horizontal line
        { 't', "\u251c" }, // ├ Left tee
        { 'u', "\u2524" }, // ┤ Right tee
        { 'v', "\u2534" }, // ┴ Bottom tee
        { 'w', "\u252c" }, // ┬ Top tee
        { 'x', "\u2502" }, // │ Vertical line
        
        // Special Characters
        { '`', "\u25c6" }, // ◆ Diamond
        { 'a', "\u2592" }, // ▒ Checkerboard (stipple)
        { 'b', "\u2409" }, // ␉ HT symbol
        { 'c', "\u240c" }, // ␌ FF symbol
        { 'd', "\u240d" }, // ␍ CR symbol
        { 'e', "\u240a" }, // ␊ LF symbol
        { 'f', "\u00b0" }, // ° Degree symbol
        { 'g', "\u00b1" }, // ± Plus/minus
        { 'h', "\u2424" }, // ␤ Newline symbol
        { 'i', "\u240b" }, // ␋ Vertical tab symbol
        { 'o', "\u23ba" }, // ⎺ Scan line 1
        { 'p', "\u23bb" }, // ⎻ Scan line 3
        { 'r', "\u23bc" }, // ⎼ Scan line 7
        { 's', "\u23bd" }, // ⎽ Scan line 9
        { 'y', "\u2264" }, // ≤ Less than or equal
        { 'z', "\u2265" }, // ≥ Greater than or equal
        { '{', "\u03c0" }, // π Pi
        { '|', "\u2260" }, // ≠ Not equal
        { '}', "\u00a3" }, // £ UK pound sign
        { '~', "\u00b7" }, // · Centered dot/bullet
    };

    /// <summary>
    /// UK character set.
    /// Maps # to pound symbol.
    /// </summary>
    public static readonly Dictionary<char, string> UKCharset = new()
    {
        { '#', "\u00a3" } // £
    };

    /// <summary>
    /// The DEC national replacement character sets (NRCS), by their designator.
    /// </summary>
    /// <remarks>
    /// <para>Each remaps a handful of positions in the ASCII range and leaves the rest alone --
    /// which is what makes them REPLACEMENT sets rather than alphabets, and why a missing one is
    /// invisible: the text comes out almost right, with the accented positions quietly reading as
    /// # @ [ ] { } instead.</para>
    ///
    /// <para>They apply only while DECNRCM (mode 42) is set. With it reset the designation is
    /// still remembered, but the set behaves as ASCII -- which is what the mode is for.</para>
    /// </remarks>
    public static readonly Dictionary<string, Dictionary<char, string>> National = new()
    {
        // French
        ["R"] = new()
        {
            { '#', "\u00a3" },
            { '@', "\u00e0" },
            { '[', "\u00b0" },
            { '\\', "\u00e7" },
            { ']', "\u00a7" },
            { '{', "\u00e9" },
            { '|', "\u00f9" },
            { '}', "\u00e8" },
            { '~', "\u00a8" },
        },
        // German
        ["K"] = new()
        {
            { '@', "\u00a7" },
            { '[', "\u00c4" },
            { '\\', "\u00d6" },
            { ']', "\u00dc" },
            { '{', "\u00e4" },
            { '|', "\u00f6" },
            { '}', "\u00fc" },
            { '~', "\u00df" },
        },
        // Swedish
        ["H"] = new()
        {
            { '@', "\u00c9" },
            { '[', "\u00c4" },
            { '\\', "\u00d6" },
            { ']', "\u00c5" },
            { '^', "\u00dc" },
            { '`', "\u00e9" },
            { '{', "\u00e4" },
            { '|', "\u00f6" },
            { '}', "\u00e5" },
            { '~', "\u00fc" },
        },
        // Italian
        ["Y"] = new()
        {
            { '#', "\u00a3" },
            { '@', "\u00a7" },
            { '[', "\u00b0" },
            { '\\', "\u00e7" },
            { ']', "\u00e9" },
            { '`', "\u00f9" },
            { '{', "\u00e0" },
            { '|', "\u00f2" },
            { '}', "\u00e8" },
            { '~', "\u00ec" },
        },
        // Spanish
        ["Z"] = new()
        {
            { '#', "\u00a3" },
            { '@', "\u00a7" },
            { '[', "\u00a1" },
            { '\\', "\u00d1" },
            { ']', "\u00bf" },
            { '{', "\u00b0" },
            { '|', "\u00f1" },
            { '}', "\u00e7" },
        },
        // Norwegian/Danish
        ["E"] = new()
        {
            { '@', "\u00c4" },
            { '[', "\u00c6" },
            { '\\', "\u00d8" },
            { ']', "\u00c5" },
            { '^', "\u00dc" },
            { '`', "\u00e4" },
            { '{', "\u00e6" },
            { '|', "\u00f8" },
            { '}', "\u00e5" },
            { '~', "\u00fc" },
        },
        // Dutch
        ["4"] = new()
        {
            { '#', "\u00a3" },
            { '@', "\u00be" },
            { '[', "\u0133" },
            { '\\', "\u00bd" },
            { ']', "\u007c" },
            { '{', "\u00a8" },
            { '|', "\u0066" },
            { '}', "\u00bc" },
            { '~', "\u00b4" },
        },
        // Swiss
        ["="] = new()
        {
            { '#', "\u00f9" },
            { '@', "\u00e0" },
            { '[', "\u00e9" },
            { '\\', "\u00e7" },
            { ']', "\u00ea" },
            { '^', "\u00ee" },
            { '_', "\u00e8" },
            { '`', "\u00f4" },
            { '{', "\u00e4" },
            { '|', "\u00f6" },
            { '}', "\u00fc" },
            { '~', "\u00fb" },
        },
        // Finnish
        ["C"] = new()
        {
            { '[', "\u00c4" },
            { '\\', "\u00d6" },
            { ']', "\u00c5" },
            { '^', "\u00dc" },
            { '`', "\u00e9" },
            { '{', "\u00e4" },
            { '|', "\u00f6" },
            { '}', "\u00e5" },
            { '~', "\u00fc" },
        },
    };

    /// <summary>
    /// The alternate designators DEC gives some national sets.
    /// </summary>
    /// <remarks>
    /// Several sets have two spellings, and a program is entitled to send either -- the primary DA
    /// advertises national replacement sets without saying which spelling. An unregistered one
    /// falls through to ASCII, which is the failure this file's remarks describe: the text comes
    /// out almost right, with the accented positions quietly reading as ASCII.
    ///
    /// French Canadian (Q, 9) and Portuguese (%6) are not here because they are tables this
    /// terminal does not have yet, not spellings of tables it does.
    /// </remarks>
    private static readonly Dictionary<string, string> NationalAliases = new()
    {
        ["f"] = "R",   // French
        ["7"] = "H",   // Swedish
        ["5"] = "C",   // Finnish
        ["6"] = "E",   // Norwegian/Danish
        ["`"] = "E",   // Norwegian/Danish, the other spelling
    };

    /// <summary>
    /// ASCII character set (no translation).
    /// Null dictionary means pass-through.
    /// </summary>
    public static readonly Dictionary<char, string>? ASCII = null;

    /// <summary>
    /// Gets the charset translation table by name.
    /// </summary>
    /// <param name="name">Charset identifier: "0" (DEC Graphics), "A" (UK), "B" (US ASCII)</param>
    /// <returns>Character translation dictionary, or null for pass-through</returns>
    public static Dictionary<char, string>? GetCharset(string name, bool nationalReplacement = false)
    {
        switch (name)
        {
            case "0": return VT100LineDrawing;  // DEC Special Graphics
            case "A": return UKCharset;          // UK
            case "B": return ASCII;              // US ASCII (default)
        }

        // The national sets are gated on DECNRCM, so the same designation means different
        // things depending on the mode -- which is why the caller re-resolves what it has
        // designated when the mode changes rather than resolving once at designation time.
        if (nationalReplacement)
        {
            var id = NationalAliases.TryGetValue(name, out var primary) ? primary : name;
            if (National.TryGetValue(id, out var national))
                return national;
        }

        // Anything else is ASCII. That includes a national set while NRC is off, and it is
        // the honest answer for a designation this terminal does not implement.
        return ASCII;
    }

    /// <summary>
    /// Translates a character using the specified charset.
    /// </summary>
    /// <param name="c">Character to translate</param>
    /// <param name="charset">Charset dictionary (null means no translation)</param>
    /// <returns>Translated string (may be multi-byte Unicode)</returns>
    public static string TranslateChar(char c, Dictionary<char, string>? charset)
    {
        if (charset == null || !charset.TryGetValue(c, out var translated))
            return CodePointText.Get(c);   // cached: c.ToString() allocated once per printed character
        
        return translated;
    }
}
