namespace XTerm.NET.Common;

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
/// Core mode constants for terminal state.
/// </summary>
public static class CoreModes
{
    public const int INSERT_MODE = 4;
    public const int AUTO_WRAP_MODE = 7;
    public const int SHOW_CURSOR = 25;
    public const int APP_CURSOR_KEYS = 1;
    public const int APP_KEYPAD = 66;
    public const int BRACKETED_PASTE_MODE = 2004;
    public const int ORIGIN = 6;
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
    public const int DEFAULT_ATTR = (0 << 18) | (257 << 9) | 256;
    public const int DEFAULT_ATTR_DATA_FG = 256;
    public const int DEFAULT_ATTR_DATA_BG = 257;
    
    public const int CHAR_DATA_ATTR_INDEX = 0;
    public const int CHAR_DATA_CHAR_INDEX = 1;
    public const int CHAR_DATA_WIDTH_INDEX = 2;
    public const int CHAR_DATA_CODE_INDEX = 3;
    
    public const int NULL_CELL_CODE = 0x0020;
    public const int NULL_CELL_WIDTH = 1;
    public const char NULL_CELL_CHAR = ' ';
    
    public const int WHITESPACE_CELL_CODE = 0x0020;
    public const int WHITESPACE_CELL_WIDTH = 1;
    public const char WHITESPACE_CELL_CHAR = ' ';
    
    public const long MAX_BUFFER_SIZE = 4294967295;
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
