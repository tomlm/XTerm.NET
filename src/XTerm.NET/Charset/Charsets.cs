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
