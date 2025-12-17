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
/// Core mode constants for terminal state.
/// </summary>
[Obsolete("Use TerminalMode enum instead for type safety and IntelliSense support.")]
public static class CoreModes
{
    // ANSI Modes (SM/RM - Set Mode / Reset Mode)
    public const int InsertMode = 4;           // IRM - Insert/Replace Mode
    public const int AutoWrapMode = 7;        // DECAWM - Automatic Wrap
    
    // DEC Private Modes (DECSET/DECRST - CSI ? Pm h / CSI ? Pm l)
    public const int AppCursorKeys = 1;       // DECCKM - Application Cursor Keys
    public const int AnsiVt52 = 2;            // DECANM - ANSI/VT52 Mode
    public const int ColumnMode = 3;          // DECCOLM - Column Mode (80/132)
    public const int SmoothScroll = 4;        // DECSCLM - Smooth Scroll
    public const int Origin = 6;               // DECOM - Origin Mode
    public const int Wraparound = 7;           // DECAWM - Wraparound Mode
    public const int AutoRepeat = 8;          // DECARM - Auto Repeat
    public const int ShowCursor = 25;         // DECTCEM - Text Cursor Enable
    public const int AppKeypad = 66;          // DECNKM - Application Keypad
    public const int BackspaceKey = 67;       // DECBKM - Backarrow Key
    public const int BracketedPasteMode = 2004; // Bracketed Paste Mode
    
    // Buffer Switching Modes
    public const int AltBuffer = 47;          // Use Alternate Screen Buffer
    public const int AltBufferCursor = 1047; // Use Alternate Screen Buffer (with cursor save/restore)
    public const int AltBufferFull = 1049;   // Save cursor and use Alternate Screen Buffer
    
    // Mouse Tracking Modes
    public const int MouseReportClick = 9;       // X10 Mouse Mode
    public const int MouseReportNormal = 1000;   // VT200 Mouse Mode
    public const int MouseReportHighlight = 1001; // Highlight Mouse Mode
    public const int MouseReportButtonEvent = 1002; // Button Event Mouse Mode
    public const int MouseReportAnyEvent = 1003; // Any Event Mouse Mode
    public const int MouseReportFocus = 1004;    // Focus Event Mode
    public const int MouseReportUtf8 = 1005;     // UTF-8 Extended Mode
    public const int MouseReportSgr = 1006;      // SGR Extended Mode
    public const int MouseReportUrxvt = 1015;    // URXVT Extended Mode
    public const int MouseReportPixel = 1016;    // Pixel Position Mode
    
    // Other Modes
    public const int SendFocusEvents = 1004; // Focus In/Out Events
    
    // Old SCREAMING_SNAKE_CASE names for backward compatibility
    [Obsolete("Use InsertMode instead")] public const int INSERT_MODE = InsertMode;
    [Obsolete("Use AutoWrapMode instead")] public const int AUTO_WRAP_MODE = AutoWrapMode;
    [Obsolete("Use AppCursorKeys instead")] public const int APP_CURSOR_KEYS = AppCursorKeys;
    [Obsolete("Use AnsiVt52 instead")] public const int ANSI_VT52 = AnsiVt52;
    [Obsolete("Use ColumnMode instead")] public const int COLUMN_MODE = ColumnMode;
    [Obsolete("Use SmoothScroll instead")] public const int SMOOTH_SCROLL = SmoothScroll;
    [Obsolete("Use Origin instead")] public const int ORIGIN = Origin;
    [Obsolete("Use Wraparound instead")] public const int WRAPAROUND = Wraparound;
    [Obsolete("Use AutoRepeat instead")] public const int AUTO_REPEAT = AutoRepeat;
    [Obsolete("Use ShowCursor instead")] public const int SHOW_CURSOR = ShowCursor;
    [Obsolete("Use AppKeypad instead")] public const int APP_KEYPAD = AppKeypad;
    [Obsolete("Use BackspaceKey instead")] public const int BACKSPACE_KEY = BackspaceKey;
    [Obsolete("Use BracketedPasteMode instead")] public const int BRACKETED_PASTE_MODE = BracketedPasteMode;
    [Obsolete("Use AltBuffer instead")] public const int ALT_BUFFER = AltBuffer;
    [Obsolete("Use AltBufferCursor instead")] public const int ALT_BUFFER_CURSOR = AltBufferCursor;
    [Obsolete("Use AltBufferFull instead")] public const int ALT_BUFFER_FULL = AltBufferFull;
    [Obsolete("Use MouseReportClick instead")] public const int MOUSE_REPORT_CLICK = MouseReportClick;
    [Obsolete("Use MouseReportNormal instead")] public const int MOUSE_REPORT_NORMAL = MouseReportNormal;
    [Obsolete("Use MouseReportHighlight instead")] public const int MOUSE_REPORT_HIGHLIGHT = MouseReportHighlight;
    [Obsolete("Use MouseReportButtonEvent instead")] public const int MOUSE_REPORT_BTN_EVENT = MouseReportButtonEvent;
    [Obsolete("Use MouseReportAnyEvent instead")] public const int MOUSE_REPORT_ANY_EVENT = MouseReportAnyEvent;
    [Obsolete("Use MouseReportFocus instead")] public const int MOUSE_REPORT_FOCUS = MouseReportFocus;
    [Obsolete("Use MouseReportUtf8 instead")] public const int MOUSE_REPORT_UTF8 = MouseReportUtf8;
    [Obsolete("Use MouseReportSgr instead")] public const int MOUSE_REPORT_SGR = MouseReportSgr;
    [Obsolete("Use MouseReportUrxvt instead")] public const int MOUSE_REPORT_URXVT = MouseReportUrxvt;
    [Obsolete("Use MouseReportPixel instead")] public const int MOUSE_REPORT_PIXEL = MouseReportPixel;
    [Obsolete("Use SendFocusEvents instead")] public const int SEND_FOCUS_EVENTS = SendFocusEvents;
}

/// <summary>
/// OSC (Operating System Command) sequence identifiers.
/// </summary>
[Obsolete("Use OscCommand enum instead for type safety and IntelliSense support.")]
public static class OscCommands
{
    public const string SET_ICON_AND_TITLE = "0";      // Set icon name and window title
    public const string SET_ICON_NAME = "1";           // Set icon name
    public const string SET_WINDOW_TITLE = "2";        // Set window title
    public const string SET_X_PROPERTY = "3";          // Set X property
    public const string CHANGE_COLOR = "4";            // Change color palette
    public const string CHANGE_SPECIAL_COLOR = "5";    // Change special color
    public const string ENABLE_SPECIAL_COLOR = "6";    // Enable special color
    public const string CURRENT_DIRECTORY = "7";       // Current working directory
    public const string HYPERLINK = "8";               // Hyperlink
    public const string GROWL_NOTIFICATION = "9";      // iTerm2 growl notification
    public const string FOREGROUND_COLOR = "10";       // Set/query foreground color
    public const string BACKGROUND_COLOR = "11";       // Set/query background color
    public const string CURSOR_COLOR = "12";           // Set/query cursor color
    public const string MOUSE_FOREGROUND = "13";       // Set/query mouse foreground
    public const string MOUSE_BACKGROUND = "14";       // Set/query mouse background
    public const string TEKTRONIX_FOREGROUND = "15";   // Tektronix foreground
    public const string TEKTRONIX_BACKGROUND = "16";   // Tektronix background
    public const string HIGHLIGHT_BACKGROUND = "17";   // Highlight background
    public const string TEKTRONIX_CURSOR = "18";       // Tektronix cursor color
    public const string HIGHLIGHT_FOREGROUND = "19";   // Highlight foreground
    public const string CLIPBOARD = "52";              // Clipboard operations
    public const string RESET_COLOR = "104";           // Reset color palette
    public const string RESET_SPECIAL_COLOR = "105";   // Reset special color
    public const string RESET_FOREGROUND = "110";      // Reset foreground color
    public const string RESET_BACKGROUND = "111";      // Reset background color
    public const string RESET_CURSOR = "112";          // Reset cursor color
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
    
    // Old SCREAMING_SNAKE_CASE names for backward compatibility
    [Obsolete("Use DefaultAttr instead")] public const int DEFAULT_ATTR = DefaultAttr;
    [Obsolete("Use DefaultAttrDataFg instead")] public const int DEFAULT_ATTR_DATA_FG = DefaultAttrDataFg;
    [Obsolete("Use DefaultAttrDataBg instead")] public const int DEFAULT_ATTR_DATA_BG = DefaultAttrDataBg;
    [Obsolete("Use CharDataAttrIndex instead")] public const int CHAR_DATA_ATTR_INDEX = CharDataAttrIndex;
    [Obsolete("Use CharDataCharIndex instead")] public const int CHAR_DATA_CHAR_INDEX = CharDataCharIndex;
    [Obsolete("Use CharDataWidthIndex instead")] public const int CHAR_DATA_WIDTH_INDEX = CharDataWidthIndex;
    [Obsolete("Use CharDataCodeIndex instead")] public const int CHAR_DATA_CODE_INDEX = CharDataCodeIndex;
    [Obsolete("Use NullCellCode instead")] public const int NULL_CELL_CODE = NullCellCode;
    [Obsolete("Use NullCellWidth instead")] public const int NULL_CELL_WIDTH = NullCellWidth;
    [Obsolete("Use NullCellChar instead")] public const char NULL_CELL_CHAR = NullCellChar;
    [Obsolete("Use WhitespaceCellCode instead")] public const int WHITESPACE_CELL_CODE = WhitespaceCellCode;
    [Obsolete("Use WhitespaceCellWidth instead")] public const int WHITESPACE_CELL_WIDTH = WhitespaceCellWidth;
    [Obsolete("Use WhitespaceCellChar instead")] public const char WHITESPACE_CELL_CHAR = WhitespaceCellChar;
    [Obsolete("Use MaxBufferSize instead")] public const long MAX_BUFFER_SIZE = MaxBufferSize;
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
