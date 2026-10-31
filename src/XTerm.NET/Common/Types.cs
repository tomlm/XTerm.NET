namespace XTerm.Common;

/// <summary>
/// Identifies the active terminal buffer.
/// </summary>
public enum BufferType
{
    Normal,
    Alternate
}

/// <summary>
/// Identifies why a terminal line left the active viewport.
/// </summary>
public enum LineExitReason
{
    Scrolled,
    BufferDeactivated
}

/// <summary>
/// Cursor style for the terminal.
/// </summary>
public enum CursorStyle
{
    Block,
    Underline,
    Bar
}

/// <summary>
/// Cursor blink state.
/// </summary>
public enum CursorBlinkState
{
    Static,
    Blink
}

/// <summary>
/// Underline style for text.
/// </summary>
public enum UnderlineStyle
{
    None = 0,
    Single = 1,
    Double = 2,
    Curly = 3,
    Dotted = 4,
    Dashed = 5
}

/// <summary>
/// Character set handling modes.
/// </summary>
public enum CharsetMode
{
    G0,
    G1,
    G2,
    G3
}

/// <summary>
/// How a cell's foreground, background or underline colour is to be read.
/// </summary>
/// <remarks>
/// <para><see cref="Palette256"/> and <see cref="RGB"/> are the values
/// <see cref="Buffer.AttributeData.GetFgColorMode"/>, <see cref="Buffer.AttributeData.GetBgColorMode"/>
/// and <see cref="Buffer.AttributeData.TryGetUnderlineColor"/> report, and always have: 0 for ANY
/// palette index -- the sixteen, the 256-colour palette, or the default colours 256 and 257 -- and
/// 1 for RGB.</para>
/// <para><see cref="Palette16"/> and <see cref="Default"/> are only ever returned by
/// <see cref="Buffer.AttributeData.GetFgColorKind"/> and
/// <see cref="Buffer.AttributeData.GetBgColorKind"/>, which say precisely which kind of colour a
/// cell holds. The two palettes are told apart because they render differently: bold text in a
/// <see cref="Palette16"/> colour 0-7 is drawn bright under
/// <see cref="Options.TerminalOptions.DrawBoldTextInBrightColors"/>, while a
/// <see cref="Palette256"/> index is drawn as the program named it, even one below 16. This is
/// xterm.js's split between <c>CM_P16</c> and <c>CM_P256</c>.</para>
/// </remarks>
public enum ColorMode
{
    /// <summary>
    /// From the int mode accessors: any palette index, or the default colour. From
    /// <c>GetFgColorKind</c>/<c>GetBgColorKind</c>: an index 0-255 named by SGR 38;5, 48;5 or 58;5.
    /// </summary>
    Palette256 = 0,

    /// <summary>A direct 24-bit colour, 0xRRGGBB, from SGR 38;2, 48;2 or 58;2.</summary>
    RGB = 1,

    /// <summary>An index 0-15 into the palette, from SGR 30-37, 40-47, 90-97 or 100-107.</summary>
    Palette16 = 2,

    /// <summary>The terminal's default foreground or background (SGR 39 or 49).</summary>
    Default = 3,
}

/// <summary>
/// Extended attributes flags.
/// </summary>
[Flags]
public enum ExtAttrFlags
{
    None = 0,
    Underline = 1 << 0,
    Foreground = 1 << 1,
    Background = 1 << 2
}

/// <summary>
/// Content flags for buffer cells.
/// </summary>
[Flags]
public enum Content
{
    None = 0,
    IsWide = 1 << 0,
    HasWidth = 1 << 1,
    HasContent = 1 << 2
}

/// <summary>
/// Cell attribute flags.
/// </summary>
[Flags]
public enum AttributeFlags
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Blink = 1 << 4,
    Inverse = 1 << 5,
    Invisible = 1 << 6,
    Strikethrough = 1 << 7,
    Overline = 1 << 8
}

/// <summary>
/// Constants for the terminal.
/// </summary>
public static class Constants
{
    public const int DefaultAttr = (0 << 18) | (257 << 9) | 256;
    public const int DefaultAttrDataFg = 256;
    public const int DefaultAttrDataBg = 257;
    
    public const int CharDataAttrIndex = 0;
    public const int CharDataCharIndex = 1;
    public const int CharDataWidthIndex = 2;
    public const int CharDataCodeIndex = 3;
    
    public const long MaxBufferSize = 4294967295;
}

/// <summary>
/// Parser state for escape sequence parsing.
/// </summary>
public enum ParserState
{
    Ground = 0,
    Escape = 1,
    EscapeIntermediate = 2,
    CsiEntry = 3,
    CsiParam = 4,
    CsiIntermediate = 5,
    CsiIgnore = 6,
    SosPmApcString = 7,
    OscString = 8,
    DcsEntry = 9,
    DcsParam = 10,
    DcsIgnore = 11,
    DcsPassthrough = 12,
    DcsIntermediate = 13,

    /// <summary>
    /// The payload of an APC sequence, which is the Kitty graphics transport.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SosPmApcString"/>, which stays the discard path for SOS and PM.
    /// Only "ESC _" comes here, because only APC carries something worth reading.
    /// </remarks>
    ApcString = 14
}

/// <summary>
/// Parser action to take.
/// </summary>
public enum ParserAction
{
    Ignore = 0,
    Error = 1,
    Print = 2,
    Execute = 3,
    OscStart = 4,
    OscPut = 5,
    OscEnd = 6,
    CsiDispatch = 7,
    Param = 8,
    Collect = 9,
    EscDispatch = 10,
    Clear = 11,
    DcsHook = 12,
    DcsPut = 13,
    DcsUnhook = 14
}

/// <summary>
/// Window information request types for OSC window queries.
/// </summary>
public enum WindowInfoRequest
{
    /// <summary>
    /// Request window position (x, y coordinates).
    /// </summary>
    Position,
    
    /// <summary>
    /// Request window size in pixels.
    /// </summary>
    SizePixels,
    
    /// <summary>
    /// Request window size in characters (columns x rows).
    /// </summary>
    SizeCharacters,
    
    /// <summary>
    /// Request screen size in pixels.
    /// </summary>
    ScreenSizePixels,
    
    /// <summary>
    /// Request cell size in pixels.
    /// </summary>
    CellSizePixels,
    
    /// <summary>
    /// Request window title.
    /// </summary>
    Title,
    
    /// <summary>
    /// Request icon title.
    /// </summary>
    IconTitle,
    
    /// <summary>
    /// Request window state (normal, minimized, maximized, fullscreen).
    /// </summary>
    State
}
