namespace XTerm.Common;

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
/// Terminal color mode.
/// </summary>
public enum ColorMode
{
    Palette256,
    RGB
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
    
    public const int NullCellCode = 0x0020;
    public const int NullCellWidth = 1;
    public const char NullCellChar = ' ';
    
    public const int WhitespaceCellCode = 0x0020;
    public const int WhitespaceCellWidth = 1;
    public const char WhitespaceCellChar = ' ';
    
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
    DcsPassthrough = 12
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
