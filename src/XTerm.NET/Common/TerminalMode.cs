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
    /// Reverse Video (DECSCNM).
    /// When set, the entire screen is displayed in reverse video.
    /// </summary>
    ReverseVideo = 5,
    
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
    /// National Replacement Character Set Mode (DECNRCM).
    /// When set, enables national replacement character sets.
    /// </summary>
    NationalCharset = 42,

    /// <summary>
    /// Sixel Display Mode (DECSDM).
    /// </summary>
    /// <remarks>
    /// The sense of this one reads backwards, and the name is why. Reset -- the default -- gives
    /// "sixel scrolling": an image is drawn at the cursor, scrolls the screen if it runs off the
    /// bottom, and leaves the cursor below itself. Set gives the older display behaviour: the
    /// image is pinned to the top-left of the screen, clipped rather than scrolled, and the cursor
    /// does not move.
    /// </remarks>
    SixelDisplayMode = 80,
    
    /// <summary>
    /// Reverse Wraparound Mode.
    /// When set, allows backspace to wrap from column 0 to the previous line.
    /// </summary>
    ReverseWraparound = 45,
    
    /// <summary>
    /// Application Keypad (DECNKM).
    /// </summary>
    AppKeypad = 66,
    
    /// <summary>
    /// Backarrow Key Mode (DECBKM).
    /// </summary>
    BackspaceKey = 67,

    /// <summary>
    /// DECLRMM — left and right margin mode. While set, <c>CSI Pl ; Pr s</c> is DECSLRM and sets the
    /// margins; while reset, that same sequence is Save Cursor.
    /// </summary>
    LeftRightMargin = 69,
    
    /// <summary>
    /// Bracketed Paste Mode.
    /// </summary>
    BracketedPasteMode = 2004,

    /// <summary>
    /// Bracketed paste MIME (private mode 5522): a paste arrives as an unsolicited Kitty
    /// clipboard read response listing the MIME types on offer, with a single-use token the
    /// application redeems to fetch the ones it wants. Takes precedence over
    /// <see cref="BracketedPasteMode"/>; the terminal must never send both for one paste.
    /// </summary>
    PasteNotification = 5522,
    
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
    /// Interpret Meta/Alt key (eightBitInput).
    /// When set, the Meta key sets the eighth bit of input characters.
    /// </summary>
    EightBitInput = 1034,

    /// <summary>
    /// Enable special modifiers for Alt and NumLock keys (numLock).
    /// </summary>
    NumLock = 1035,

    /// <summary>
    /// Meta sends escape (metaSendsEscape).
    /// When set, pressing Meta+key sends ESC followed by the key.
    /// </summary>
    MetaSendsEscape = 1036,

    /// <summary>
    /// Alt sends escape (altSendsEscape).
    /// When set, pressing Alt+key sends ESC followed by the key.
    /// </summary>
    AltSendsEscape = 1039,

    /// <summary>
    /// Sixel private colour registers.
    /// </summary>
    /// <remarks>
    /// Set -- the default -- gives each image its own colour registers, so one picture cannot
    /// recolour the one before it. Reset shares a single set across images, which is how a VT340
    /// behaved and what a handful of old programs rely on.
    /// </remarks>
    SixelPrivateColorRegisters = 1070,

    /// <summary>
    /// Synchronized output: hold the display still until the application has finished drawing.
    /// </summary>
    /// <remarks>
    /// <para>Set means "an atomic update has begun" and reset means "it is finished". A full-screen
    /// application redraws in many writes, and a renderer that paints between them shows a frame
    /// half old and half new -- the tearing you see when a TUI repaints under load.</para>
    /// <para>The emulator only reports the state; holding the frame is the renderer's decision, and
    /// so is the timeout that has to bound it. Without one, an application that sets this and then
    /// crashes would freeze the display for good.</para>
    /// </remarks>
    SynchronizedOutput = 2026,

    /// <summary>
    /// Win32 Input Mode.
    /// </summary>
    Win32InputMode = 9001,

    /// <summary>
    /// Leave the cursor to the right of a Sixel image rather than below it.
    /// </summary>
    SixelCursorRight = 8452
}
