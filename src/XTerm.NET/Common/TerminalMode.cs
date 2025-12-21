namespace XTerm.Common;

/// <summary>
/// Terminal mode identifiers for ANSI and DEC private modes.
/// Used with CSI h (Set Mode) and CSI l (Reset Mode) sequences.
/// </summary>
public enum TerminalMode
{
    // ANSI Modes (SM/RM - Set Mode / Reset Mode)
    /// <summary>
    /// Insert/Replace Mode (IRM).
    /// </summary>
    InsertMode = 4,
    
    /// <summary>
    /// Automatic Wrap Mode (DECAWM).
    /// </summary>
    AutoWrapMode = 7,
    
    // DEC Private Modes (DECSET/DECRST - CSI ? Pm h / CSI ? Pm l)
    /// <summary>
    /// Application Cursor Keys (DECCKM).
    /// </summary>
    AppCursorKeys = 1,
    
    /// <summary>
    /// ANSI/VT52 Mode (DECANM).
    /// </summary>
    AnsiVt52 = 2,
    
    /// <summary>
    /// Column Mode - 80/132 columns (DECCOLM).
    /// </summary>
    ColumnMode = 3,
    
    /// <summary>
    /// Smooth Scroll (DECSCLM).
    /// </summary>
    SmoothScroll = 4,
    
    /// <summary>
    /// Origin Mode (DECOM).
    /// </summary>
    Origin = 6,
    
    /// <summary>
    /// Wraparound Mode (DECAWM).
    /// </summary>
    Wraparound = 7,
    
    /// <summary>
    /// Auto Repeat (DECARM).
    /// </summary>
    AutoRepeat = 8,
    
    /// <summary>
    /// Text Cursor Enable (DECTCEM).
    /// </summary>
    ShowCursor = 25,
    
    /// <summary>
    /// Application Keypad (DECNKM).
    /// </summary>
    AppKeypad = 66,
    
    /// <summary>
    /// Backarrow Key Mode (DECBKM).
    /// </summary>
    BackspaceKey = 67,
    
    /// <summary>
    /// Bracketed Paste Mode.
    /// </summary>
    BracketedPasteMode = 2004,
    
    // Buffer Switching Modes
    /// <summary>
    /// Use Alternate Screen Buffer.
    /// </summary>
    AltBuffer = 47,
    
    /// <summary>
    /// Use Alternate Screen Buffer with cursor save/restore.
    /// </summary>
    AltBufferCursor = 1047,
    
    /// <summary>
    /// Save cursor and use Alternate Screen Buffer.
    /// </summary>
    AltBufferFull = 1049,
    
    // Mouse Tracking Modes
    /// <summary>
    /// X10 Mouse Mode.
    /// </summary>
    MouseReportClick = 9,
    
    /// <summary>
    /// VT200 Mouse Mode.
    /// </summary>
    MouseReportNormal = 1000,
    
    /// <summary>
    /// Highlight Mouse Mode.
    /// </summary>
    MouseReportHighlight = 1001,
    
    /// <summary>
    /// Button Event Mouse Mode.
    /// </summary>
    MouseReportButtonEvent = 1002,
    
    /// <summary>
    /// Any Event Mouse Mode.
    /// </summary>
    MouseReportAnyEvent = 1003,
    
    /// <summary>
    /// Focus Event Mode.
    /// </summary>
    SendFocusEvents = 1004,
    
    /// <summary>
    /// UTF-8 Extended Mouse Mode.
    /// </summary>
    MouseReportUtf8 = 1005,
    
    /// <summary>
    /// SGR Extended Mouse Mode.
    /// </summary>
    MouseReportSgr = 1006,
    
    /// <summary>
    /// URXVT Extended Mouse Mode.
    /// </summary>
    MouseReportUrxvt = 1015,
    
    /// <summary>
    /// Pixel Position Mouse Mode.
    /// </summary>
    MouseReportPixel = 1016,

    /// <summary>
    /// Win32 Input Mode.
    /// </summary>
    Win32InputMode = 9001
}
