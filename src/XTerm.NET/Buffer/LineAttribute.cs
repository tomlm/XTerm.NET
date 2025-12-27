namespace XTerm.Buffer;

/// <summary>
/// DEC line attributes for double-width/double-height display.
/// These affect how the entire line is rendered and are set via
/// ESC # sequences (DECSWL, DECDWL, DECDHL).
/// </summary>
/// <remarks>
/// Double-width lines display each character at 2x the normal cell width,
/// meaning only cols/2 characters can be displayed on the line.
/// 
/// Double-height lines require TWO consecutive lines - one for the top half
/// and one for the bottom half. Both lines contain the same content but
/// each renders only their respective vertical half.
/// </remarks>
public enum LineAttribute
{
    /// <summary>
    /// Normal single-width, single-height line (DECSWL - ESC # 5).
    /// This is the default state for all lines.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Double-width, single-height line (DECDWL - ESC # 6).
    /// Characters display at 2x width, so only cols/2 characters fit.
    /// </summary>
    DoubleWidth = 1,

    /// <summary>
    /// Double-height line, top half (DECDHL - ESC # 3).
    /// Displays the top half of double-height characters.
    /// The next line should have DoubleHeightBottom with the same content.
    /// Also implies double-width (2x horizontal scale).
    /// </summary>
    DoubleHeightTop = 2,

    /// <summary>
    /// Double-height line, bottom half (DECDHL - ESC # 4).
    /// Displays the bottom half of double-height characters.
    /// The previous line should have DoubleHeightTop with the same content.
    /// Also implies double-width (2x horizontal scale).
    /// </summary>
    DoubleHeightBottom = 3,
}

/// <summary>
/// Extension methods for LineAttribute.
/// </summary>
public static class LineAttributeExtensions
{
    /// <summary>
    /// Returns true if the line attribute implies double-width rendering.
    /// All non-normal attributes (DoubleWidth, DoubleHeightTop, DoubleHeightBottom)
    /// render characters at 2x width.
    /// </summary>
    public static bool IsDoubleWidth(this LineAttribute attr) => attr != LineAttribute.Normal;

    /// <summary>
    /// Returns true if the line attribute is part of a double-height pair.
    /// </summary>
    public static bool IsDoubleHeight(this LineAttribute attr) =>
        attr == LineAttribute.DoubleHeightTop || attr == LineAttribute.DoubleHeightBottom;
}
