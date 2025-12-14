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
        { 'j', "\u250c" }, // ? Bottom right corner
        { 'k', "\u2510" }, // ? Top right corner
        { 'l', "\u250c" }, // ? Top left corner
        { 'm', "\u2514" }, // ? Bottom left corner
        { 'n', "\u253c" }, // ? Cross/intersection
        { 'q', "\u2500" }, // ? Horizontal line
        { 't', "\u251c" }, // ? Left tee
        { 'u', "\u2524" }, // ? Right tee
        { 'v', "\u2534" }, // ? Bottom tee
        { 'w', "\u252c" }, // ? Top tee
        { 'x', "\u2502" }, // ? Vertical line
        
        // Special Characters
        { '`', "\u25c6" }, // ? Diamond
        { 'a', "\u2592" }, // ? Checkerboard (stipple)
        { 'f', "\u00b0" }, // ° Degree symbol
        { 'g', "\u00b1" }, // ± Plus/minus
        { 'h', "\u2424" }, // ? Newline symbol
        { 'i', "\u240b" }, // ? Vertical tab symbol
        { 'o', "\u23ba" }, // ? Scan line 1
        { 'p', "\u23bb" }, // ? Scan line 3
        { 'r', "\u23bc" }, // ? Scan line 7
        { 's', "\u23bd" }, // ? Scan line 9
        { 'y', "\u2264" }, // ? Less than or equal
        { 'z', "\u2265" }, // ? Greater than or equal
        { '{', "\u03c0" }, // ? Pi
        { '|', "\u2260" }, // ? Not equal
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
    /// ASCII character set (no translation).
    /// Null dictionary means pass-through.
    /// </summary>
    public static readonly Dictionary<char, string>? ASCII = null;

    /// <summary>
    /// Gets the charset translation table by name.
    /// </summary>
    /// <param name="name">Charset identifier: "0" (DEC Graphics), "A" (UK), "B" (US ASCII)</param>
    /// <returns>Character translation dictionary, or null for pass-through</returns>
    public static Dictionary<char, string>? GetCharset(string name)
    {
        return name switch
        {
            "0" => VT100LineDrawing,  // DEC Special Graphics
            "A" => UKCharset,          // UK
            "B" => ASCII,              // US ASCII (default)
            _ => ASCII                 // Default to ASCII for unknown charsets
        };
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
            return c.ToString();
        
        return translated;
    }
}
