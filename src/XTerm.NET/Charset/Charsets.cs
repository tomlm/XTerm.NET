using System.Text;

namespace XTerm.Charset;

/// <summary>
/// Character set translation tables for VT100 special graphics and other charsets.
/// </summary>
public static class Charsets
{
    /// <summary>
    /// Default charset (no translation).
    /// </summary>
    public static readonly Dictionary<char, string> Default = new();

    /// <summary>
    /// VT100 graphics charset (line drawing characters).
    /// </summary>
    public static readonly Dictionary<char, string> Graphics = new()
    {
        ['`'] = "?", // Diamond
        ['a'] = "?", // Checkerboard
        ['b'] = "?", // HT
        ['c'] = "?", // FF
        ['d'] = "?", // CR
        ['e'] = "?", // LF
        ['f'] = "°", // Degree symbol
        ['g'] = "±", // Plus/minus
        ['h'] = "?", // NL
        ['i'] = "?", // VT
        ['j'] = "?", // Lower right corner
        ['k'] = "?", // Upper right corner
        ['l'] = "?", // Upper left corner
        ['m'] = "?", // Lower left corner
        ['n'] = "?", // Crossing lines
        ['o'] = "?", // Horizontal line - scan 1
        ['p'] = "?", // Horizontal line - scan 3
        ['q'] = "?", // Horizontal line - scan 5
        ['r'] = "?", // Horizontal line - scan 7
        ['s'] = "?", // Horizontal line - scan 9
        ['t'] = "?", // Left tee
        ['u'] = "?", // Right tee
        ['v'] = "?", // Bottom tee
        ['w'] = "?", // Top tee
        ['x'] = "?", // Vertical bar
        ['y'] = "?", // Less than or equal
        ['z'] = "?", // Greater than or equal
        ['{'] = "?", // Pi
        ['|'] = "?", // Not equal
        ['}'] = "£", // UK pound sign
        ['~'] = "·"  // Bullet
    };

    /// <summary>
    /// UK charset.
    /// </summary>
    public static readonly Dictionary<char, string> UK = new()
    {
        ['#'] = "£"
    };

    /// <summary>
    /// Gets a charset by name.
    /// </summary>
    public static Dictionary<char, string> GetCharset(string name)
    {
        return name switch
        {
            "0" => Graphics,
            "A" => UK,
            "B" => Default,
            _ => Default
        };
    }

    /// <summary>
    /// Translates a character through a charset.
    /// </summary>
    public static string Translate(char ch, Dictionary<char, string> charset)
    {
        if (charset.TryGetValue(ch, out var translated))
        {
            return translated;
        }
        return ch.ToString();
    }
}

/// <summary>
/// Unicode utilities for handling character widths and properties.
/// </summary>
public static class UnicodeUtils
{
    /// <summary>
    /// Gets the display width of a character (1 for normal, 2 for wide, 0 for combining).
    /// </summary>
    public static int GetStringCellWidth(string str)
    {
        if (string.IsNullOrEmpty(str))
            return 0;

        var width = 0;
        foreach (var rune in str.EnumerateRunes())
        {
            width += GetRuneWidth(rune);
        }
        return width;
    }

    /// <summary>
    /// Gets the display width of a single rune.
    /// </summary>
    public static int GetRuneWidth(Rune rune)
    {
        var value = rune.Value;

        // Control characters
        if (value < 0x20 || (value >= 0x7F && value < 0xA0))
            return 0;

        // Combining characters
        if (IsCombiningMark(value))
            return 0;

        // Wide characters (CJK, emoji, etc.)
        if (IsWideCharacter(value))
            return 2;

        // Default width
        return 1;
    }

    /// <summary>
    /// Checks if a code point is a combining mark.
    /// </summary>
    private static bool IsCombiningMark(int codePoint)
    {
        // Simplified combining mark detection
        return (codePoint >= 0x0300 && codePoint <= 0x036F) || // Combining Diacritical Marks
               (codePoint >= 0x1AB0 && codePoint <= 0x1AFF) || // Combining Diacritical Marks Extended
               (codePoint >= 0x1DC0 && codePoint <= 0x1DFF) || // Combining Diacritical Marks Supplement
               (codePoint >= 0x20D0 && codePoint <= 0x20FF) || // Combining Diacritical Marks for Symbols
               (codePoint >= 0xFE20 && codePoint <= 0xFE2F);   // Combining Half Marks
    }

    /// <summary>
    /// Checks if a code point is a wide character.
    /// </summary>
    private static bool IsWideCharacter(int codePoint)
    {
        // East Asian Width property
        return (codePoint >= 0x1100 && codePoint <= 0x115F) ||   // Hangul Jamo
               (codePoint >= 0x2329 && codePoint <= 0x232A) ||   // Angle brackets
               (codePoint >= 0x2E80 && codePoint <= 0x2E99) ||   // CJK Radicals Supplement
               (codePoint >= 0x2E9B && codePoint <= 0x2EF3) ||
               (codePoint >= 0x2F00 && codePoint <= 0x2FD5) ||   // Kangxi Radicals
               (codePoint >= 0x2FF0 && codePoint <= 0x2FFB) ||   // Ideographic Description Characters
               (codePoint >= 0x3000 && codePoint <= 0x303E) ||   // CJK Symbols and Punctuation
               (codePoint >= 0x3041 && codePoint <= 0x3096) ||   // Hiragana
               (codePoint >= 0x3099 && codePoint <= 0x30FF) ||   // Katakana
               (codePoint >= 0x3105 && codePoint <= 0x312F) ||   // Bopomofo
               (codePoint >= 0x3131 && codePoint <= 0x318E) ||   // Hangul Compatibility Jamo
               (codePoint >= 0x3190 && codePoint <= 0x31BA) ||   // Kanbun
               (codePoint >= 0x31C0 && codePoint <= 0x31E3) ||   // CJK Strokes
               (codePoint >= 0x31F0 && codePoint <= 0x321E) ||   // Katakana Phonetic Extensions
               (codePoint >= 0x3220 && codePoint <= 0x3247) ||   // Enclosed CJK Letters and Months
               (codePoint >= 0x3250 && codePoint <= 0x4DBF) ||   // CJK Unified Ideographs Extension A
               (codePoint >= 0x4E00 && codePoint <= 0xA48C) ||   // CJK Unified Ideographs
               (codePoint >= 0xA490 && codePoint <= 0xA4C6) ||   // Yi Radicals
               (codePoint >= 0xA960 && codePoint <= 0xA97C) ||   // Hangul Jamo Extended-A
               (codePoint >= 0xAC00 && codePoint <= 0xD7A3) ||   // Hangul Syllables
               (codePoint >= 0xF900 && codePoint <= 0xFAFF) ||   // CJK Compatibility Ideographs
               (codePoint >= 0xFE10 && codePoint <= 0xFE19) ||   // Vertical forms
               (codePoint >= 0xFE30 && codePoint <= 0xFE6B) ||   // CJK Compatibility Forms
               (codePoint >= 0xFF01 && codePoint <= 0xFF60) ||   // Fullwidth ASCII variants
               (codePoint >= 0xFFE0 && codePoint <= 0xFFE6) ||   // Fullwidth symbol variants
               (codePoint >= 0x1B000 && codePoint <= 0x1B12F) || // Kana Supplement/Extended
               (codePoint >= 0x1B170 && codePoint <= 0x1B2FB) || // Nushu
               (codePoint >= 0x1F200 && codePoint <= 0x1F251) || // Enclosed Ideographic Supplement
               (codePoint >= 0x20000 && codePoint <= 0x2FFFD) || // CJK Unified Ideographs Extension B-E
               (codePoint >= 0x30000 && codePoint <= 0x3FFFD);   // CJK Unified Ideographs Extension F
    }

    /// <summary>
    /// Checks if a character is a null character.
    /// </summary>
    public static bool IsNullChar(int codePoint)
    {
        return codePoint == 0 || codePoint == 0x0020;
    }

    /// <summary>
    /// Checks if a character is whitespace.
    /// </summary>
    public static bool IsWhitespace(int codePoint)
    {
        return codePoint == 0x0020 || // Space
               codePoint == 0x0009 || // Tab
               codePoint == 0x00A0 || // Non-breaking space
               codePoint == 0x1680 || // Ogham space mark
               (codePoint >= 0x2000 && codePoint <= 0x200B) || // Various spaces
               codePoint == 0x202F || // Narrow no-break space
               codePoint == 0x205F || // Medium mathematical space
               codePoint == 0x3000;   // Ideographic space
    }

    /// <summary>
    /// Normalizes a string for terminal display.
    /// </summary>
    public static string Normalize(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        // Normalize to NFC (Canonical Decomposition followed by Canonical Composition)
        return str.Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Checks if a code point is a control character.
    /// </summary>
    public static bool IsControlChar(int codePoint)
    {
        return (codePoint < 0x20) || (codePoint >= 0x7F && codePoint < 0xA0);
    }

    /// <summary>
    /// Checks if a code point is printable.
    /// </summary>
    public static bool IsPrintable(int codePoint)
    {
        return !IsControlChar(codePoint);
    }
}
