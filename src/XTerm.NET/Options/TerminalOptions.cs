using XTerm.Common;

namespace XTerm.Options;

/// <summary>
/// Terminal configuration options.
/// </summary>
public class TerminalOptions : ICloneable
{
    /// <summary>
    /// Number of columns in the terminal.
    /// </summary>
    public int Cols { get; set; } = 80;

    /// <summary>
    /// Number of rows in the terminal.
    /// </summary>
    public int Rows { get; set; } = 24;

    /// <summary>
    /// Amount of scrollback in the terminal. 0 disables scrollback.
    /// </summary>
    public int Scrollback { get; set; } = 1000;

    /// <summary>
    /// Tab stop width.
    /// </summary>
    public int TabStopWidth { get; set; } = 8;

    /// <summary>
    /// Whether to enable bell sound/notification.
    /// </summary>
    public bool BellSound { get; set; } = false;

    /// <summary>
    /// Bell sound volume (0-1).
    /// </summary>
    public double BellVolume { get; set; } = 0.5;

    /// <summary>
    /// Enable bell style (sound, visual, both, none).
    /// </summary>
    public BellStyle BellStyle { get; set; } = BellStyle.None;

    /// <summary>
    /// Cursor blink rate in milliseconds.
    /// </summary>
    public int CursorBlinkRate { get; set; } = 530;

    /// <summary>
    /// Cursor style.
    /// </summary>
    public CursorStyle CursorStyle { get; set; } = CursorStyle.Block;

    /// <summary>
    /// Whether the cursor should blink.
    /// </summary>
    public bool CursorBlink { get; set; } = false;

    /// <summary>
    /// Font family.
    /// </summary>
    public string FontFamily { get; set; } = "monospace";

    /// <summary>
    /// Font size in pixels.
    /// </summary>
    public int FontSize { get; set; } = 15;

    /// <summary>
    /// Font weight.
    /// </summary>
    public string FontWeight { get; set; } = "normal";

    /// <summary>
    /// Font weight for bold text.
    /// </summary>
    public string FontWeightBold { get; set; } = "bold";

    /// <summary>
    /// Letter spacing.
    /// </summary>
    public double LetterSpacing { get; set; } = 0;

    /// <summary>
    /// Line height multiplier.
    /// </summary>
    public double LineHeight { get; set; } = 1.0;

    /// <summary>
    /// Whether to enable line wrapping.
    /// </summary>
    public bool Wraparound { get; set; } = true;

    /// <summary>
    /// Whether to convert EOL characters.
    /// </summary>
    public bool ConvertEol { get; set; } = false;

    /// <summary>
    /// Terminal type reported.
    /// </summary>
    public string TermName { get; set; } = "xterm";

    /// <summary>
    /// Whether to enable fast scrolling.
    /// </summary>
    public bool FastScrollModifier { get; set; } = false;

    /// <summary>
    /// Scroll sensitivity.
    /// </summary>
    public int ScrollSensitivity { get; set; } = 1;

    /// <summary>
    /// Whether to allow transparency.
    /// </summary>
    public bool AllowTransparency { get; set; } = false;

    /// <summary>
    /// Mac option is meta key.
    /// </summary>
    public bool MacOptionIsMeta { get; set; } = false;

    /// <summary>
    /// Right click selects word.
    /// </summary>
    public bool RightClickSelectsWord { get; set; } = true;

    /// <summary>
    /// Renderer type.
    /// </summary>
    public RendererType RendererType { get; set; } = RendererType.Canvas;

    /// <summary>
    /// Whether Sixel images are decoded and placed in the buffer.
    /// </summary>
    /// <remarks>
    /// Also governs whether the terminal advertises Sixel in its primary Device Attributes reply.
    /// Turning it off makes well-behaved applications send text instead of pictures, rather than
    /// sending pictures that get dropped.
    /// </remarks>
    public bool SixelEnabled { get; set; } = true;

    /// <summary>
    /// Whether Kitty graphics commands are honoured.
    /// </summary>
    /// <remarks>
    /// Turning it off makes the terminal refuse a query as well as a picture, so a well-behaved
    /// application falls back to text rather than sending images that get dropped.
    /// </remarks>
    public bool KittyGraphicsEnabled { get; set; } = true;

    /// <summary>Whether iTerm2 OSC 1337 inline images are honoured.</summary>
    public bool ITerm2ImagesEnabled { get; set; } = true;

    /// <summary>
    /// Whether Kitty desktop notification requests (OSC 99) are honoured.
    /// </summary>
    /// <remarks>
    /// On by default, matching every terminal that implements the protocol — kitty, Ghostty,
    /// foot, WezTerm and iTerm2 all ship desktop notifications enabled. The risk profile allows
    /// it: unlike clipboard READ-BACK, which exfiltrates data and stays opt-in, a notification
    /// hands pty-controlled text to a display-only OS API, and the worst a hostile pty writer
    /// (a remote host over ssh, say) achieves is annoyance the host can gate at display time.
    /// The default also decides discoverability: capability detectors probe with p=?, and a
    /// disabled gate deliberately refuses the query — so an off default reads as "unsupported"
    /// in every terminal comparison matrix. A host that prefers silence sets this false and the
    /// terminal refuses the query too, keeping well-behaved applications quiet rather than
    /// notifying into the void.
    /// </remarks>
    public bool KittyNotificationsEnabled { get; set; } = true;
    /// <summary>
    /// Whether applications may write to the host clipboard using OSC 52 or Kitty OSC 5522.
    /// </summary>
    public bool ClipboardWriteEnabled { get; set; } = true;

    /// <summary>
    /// Whether applications may read from the host clipboard using OSC 52 or Kitty OSC 5522.
    /// </summary>
    /// <remarks>
    /// Disabled by default because terminal output can otherwise exfiltrate clipboard contents.
    /// </remarks>
    public bool ClipboardReadEnabled { get; set; } = false;

    /// <summary>
    /// Whether control characters survive <see cref="Terminal.Paste(TerminalPaste)"/>.
    /// </summary>
    /// <remarks>
    /// <para>Off by default, matching xterm's own <c>allowPasteControls</c>. Pasted text is
    /// attacker-influenced far more often than typed input is -- it comes from a web page, a chat
    /// message, a README -- and a paste carrying <c>ESC</c> can close the bracketed-paste wrapper
    /// early with <c>ESC [ 2 0 1 ~</c> and have everything after it read as though the user typed
    /// it. That is a path from "copied a command from a website" to "ran a different command".</para>
    /// <para>While off, control characters other than tab and carriage return are dropped from
    /// pasted text — C1 as well as C0, which is deliberate rather than incidental: U+009B is
    /// eight-bit CSI and starts a sequence exactly as <c>ESC [</c> does, so filtering only C0
    /// would leave the wrapper breakable by a paste that spells its escape differently. An
    /// embedder that genuinely wants to feed escape sequences to the application should call
    /// <see cref="Terminal.Write(string)"/>, which is the unfiltered path and always was.</para>
    /// </remarks>
    public bool AllowPasteControls { get; set; } = false;

    /// <summary>
    /// Maximum decoded clipboard bytes accepted in a Kitty OSC 5522 write.
    /// </summary>
    public int MaxClipboardBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Whether the Kitty keyboard protocol sequences (CSI u) are honoured.
    /// </summary>
    /// <remarks>
    /// Turning it off makes the terminal ignore the query as well as the flags, so an application
    /// probing with CSI ? u gets no answer and stays in legacy encoding.
    /// </remarks>
    public bool KittyKeyboardEnabled { get; set; } = true;

    /// <summary>
    /// Whether mouse pointer shape requests (OSC 22) are honoured. Off by default.
    /// </summary>
    /// <remarks>
    /// Opt-in, because the emulator cannot make this work on its own: only the host can change a
    /// real pointer, and it does that by subscribing to
    /// <see cref="Terminal.PointerShapeChanged"/>. Left on by default, a host that has not wired
    /// that event would still answer the support query with a yes, and an application would go on
    /// using shapes that never appear. Turning it on is the host saying it has wired the event, so
    /// the yes is true when it is given.
    /// </remarks>
    public bool PointerShapesEnabled { get; set; }

    /// <summary>
    /// Budget for images held by client id but not currently on screen, in bytes.
    /// </summary>
    /// <remarks>
    /// Kitty transmits a picture once and may show it later, so an image can be live with no cell
    /// referencing it. Cell references cannot account for those, and without a ceiling a program
    /// could transmit without ever placing and never be collected. Oldest goes first.
    /// </remarks>
    public long MaxImageRegistryBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>Maximum number of iTerm2 OSC 1337 user variables retained.</summary>
    public int MaxUserVariables { get; set; } = 128;

    /// <summary>Maximum decoded UTF-8 bytes retained for one iTerm2 user variable.</summary>
    public int MaxUserVariableBytes { get; set; } = 4096;

    /// <summary>
    /// Width of a character cell in pixels.
    /// </summary>
    /// <remarks>
    /// The terminal is headless and cannot measure a font, so a host that renders images should
    /// set this and <see cref="CellHeightPixels"/> from its own metrics -- in device pixels, not
    /// layout units. It decides how many columns an image of a given pixel width covers, and it is
    /// the answer given to a CSI 16 t query when the host does not handle that itself. The default
    /// is a plausible 10x20 so that images placed without a host configuring anything still land
    /// somewhere sensible.
    /// </remarks>
    public int CellWidthPixels { get; set; } = 10;

    /// <summary>
    /// Height of a character cell in pixels. See <see cref="CellWidthPixels"/>.
    /// </summary>
    public int CellHeightPixels { get; set; } = 20;

    /// <summary>
    /// Device pixels per logical point on the display the terminal renders to — 2.0 on a Retina
    /// display, 1.0 (the default) where pixels are points. Set by the host beside the cell pixel
    /// metrics; iTerm2's ReportCellSize divides by it, because that query speaks points.
    /// </summary>
    public double DisplayScale { get; set; } = 1.0;

    /// <summary>
    /// Largest Sixel image accepted, in pixels. Larger ones are discarded as they decode.
    /// </summary>
    /// <remarks>
    /// A Sixel payload declares no size until it has been drawn, so without a ceiling a hostile
    /// or simply broken process can make the terminal allocate until it dies. Four megapixels is
    /// comfortably larger than a full-screen image on a high-DPI display.
    /// </remarks>
    public int MaxSixelPixels { get; set; } = 4_000_000;

    /// <summary>
    /// Budget for image pixels held live in the buffer, in bytes.
    /// </summary>
    /// <remarks>
    /// Images normally look after themselves: one is freed when the last cell showing it is
    /// overwritten or scrolls out of the scrollback. This is the backstop for the case that
    /// defeats it -- a long scrollback full of pictures, all still referenced and all still in
    /// memory. Over budget, the oldest are dropped from the buffer.
    /// </remarks>
    public long MaxImageBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// Window options handling.
    /// </summary>
    public WindowOptions WindowOptions { get; set; } = new WindowOptions();

    /// <summary>
    /// Theme colors.
    /// </summary>
    public ThemeOptions Theme { get; set; } = new ThemeOptions();

    /// <summary>
    /// Minimum contrast ratio.
    /// </summary>
    public double MinimumContrastRatio { get; set; } = 1;

    /// <summary>
    /// Whether to draw bold text in bright colors.
    /// </summary>
    public bool DrawBoldTextInBrightColors { get; set; } = true;

    /// <summary>
    /// Custom key event handler.
    /// </summary>
    public Func<KeyEvent, bool>? CustomKeyEventHandler { get; set; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public TerminalOptions()
    {
    }

    /// <summary>
    /// Copy constructor for cloning.
    /// </summary>
    public TerminalOptions(TerminalOptions other)
    {
        Cols = other.Cols;
        Rows = other.Rows;
        Scrollback = other.Scrollback;
        TabStopWidth = other.TabStopWidth;
        BellSound = other.BellSound;
        BellVolume = other.BellVolume;
        BellStyle = other.BellStyle;
        CursorBlinkRate = other.CursorBlinkRate;
        CursorStyle = other.CursorStyle;
        CursorBlink = other.CursorBlink;
        FontFamily = other.FontFamily;
        FontSize = other.FontSize;
        FontWeight = other.FontWeight;
        FontWeightBold = other.FontWeightBold;
        LetterSpacing = other.LetterSpacing;
        LineHeight = other.LineHeight;
        Wraparound = other.Wraparound;
        ConvertEol = other.ConvertEol;
        TermName = other.TermName;
        FastScrollModifier = other.FastScrollModifier;
        ScrollSensitivity = other.ScrollSensitivity;
        AllowTransparency = other.AllowTransparency;
        MacOptionIsMeta = other.MacOptionIsMeta;
        RightClickSelectsWord = other.RightClickSelectsWord;
        RendererType = other.RendererType;
        SixelEnabled = other.SixelEnabled;
        KittyGraphicsEnabled = other.KittyGraphicsEnabled;
        ITerm2ImagesEnabled = other.ITerm2ImagesEnabled;
        KittyNotificationsEnabled = other.KittyNotificationsEnabled;
        ClipboardWriteEnabled = other.ClipboardWriteEnabled;
        ClipboardReadEnabled = other.ClipboardReadEnabled;
        AllowPasteControls = other.AllowPasteControls;
        MaxClipboardBytes = other.MaxClipboardBytes;
        KittyKeyboardEnabled = other.KittyKeyboardEnabled;
        PointerShapesEnabled = other.PointerShapesEnabled;
        MaxImageRegistryBytes = other.MaxImageRegistryBytes;
        MaxUserVariables = other.MaxUserVariables;
        MaxUserVariableBytes = other.MaxUserVariableBytes;
        CellWidthPixels = other.CellWidthPixels;
        CellHeightPixels = other.CellHeightPixels;
        DisplayScale = other.DisplayScale;
        MaxSixelPixels = other.MaxSixelPixels;
        MaxImageBytes = other.MaxImageBytes;
        // Public setters make null possible at runtime even though nullable annotations discourage
        // it. Preserve the constructor's former tolerance rather than throwing from inside Clone.
        WindowOptions = other.WindowOptions is null
            ? new WindowOptions()
            : new WindowOptions(other.WindowOptions);
        Theme = other.Theme is null ? new ThemeOptions() : new ThemeOptions(other.Theme);
        MinimumContrastRatio = other.MinimumContrastRatio;
        DrawBoldTextInBrightColors = other.DrawBoldTextInBrightColors;
        CustomKeyEventHandler = other.CustomKeyEventHandler;
    }

    /// <summary>
    /// Creates a copy of this TerminalOptions.
    /// </summary>
    public TerminalOptions Clone()
    {
        return new TerminalOptions(this);
    }

    /// <summary>
    /// Explicit interface implementation for ICloneable.
    /// </summary>
    object ICloneable.Clone()
    {
        return Clone();
    }
}

/// <summary>
/// Bell style options.
/// </summary>
public enum BellStyle
{
    None,
    Sound,
    Visual,
    Both
}

/// <summary>
/// Renderer type options.
/// </summary>
public enum RendererType
{
    Canvas,
    Dom,
    WebGL
}

/// <summary>
/// Window options for OSC commands.
/// </summary>
public class WindowOptions : ICloneable
{
    public bool GetWinPosition { get; set; } = false;
    public bool GetWinSizePixels { get; set; } = false;
    public bool GetWinSizeChars { get; set; } = false;
    public bool GetScreenSizePixels { get; set; } = false;
    public bool GetCellSizePixels { get; set; } = false;
    public bool GetIconTitle { get; set; } = false;
    public bool GetWinTitle { get; set; } = false;
    public bool GetWinState { get; set; } = false;
    public bool SetWinPosition { get; set; } = false;
    public bool SetWinSizePixels { get; set; } = false;
    public bool SetWinSizeChars { get; set; } = false;
    public bool RaiseWin { get; set; } = false;
    public bool LowerWin { get; set; } = false;
    public bool RefreshWin { get; set; } = false;
    public bool RestoreWin { get; set; } = false;
    public bool MaximizeWin { get; set; } = false;
    public bool MinimizeWin { get; set; } = false;
    public bool FullscreenWin { get; set; } = false;
    public bool RequestAttention { get; set; } = false;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public WindowOptions()
    {
    }

    /// <summary>
    /// Copy constructor for cloning.
    /// </summary>
    public WindowOptions(WindowOptions other)
    {
        GetWinPosition = other.GetWinPosition;
        GetWinSizePixels = other.GetWinSizePixels;
        GetWinSizeChars = other.GetWinSizeChars;
        GetScreenSizePixels = other.GetScreenSizePixels;
        GetCellSizePixels = other.GetCellSizePixels;
        GetIconTitle = other.GetIconTitle;
        GetWinTitle = other.GetWinTitle;
        GetWinState = other.GetWinState;
        SetWinPosition = other.SetWinPosition;
        SetWinSizePixels = other.SetWinSizePixels;
        SetWinSizeChars = other.SetWinSizeChars;
        RaiseWin = other.RaiseWin;
        LowerWin = other.LowerWin;
        RefreshWin = other.RefreshWin;
        RestoreWin = other.RestoreWin;
        MaximizeWin = other.MaximizeWin;
        MinimizeWin = other.MinimizeWin;
        FullscreenWin = other.FullscreenWin;
        RequestAttention = other.RequestAttention;
    }

    /// <summary>
    /// Creates a copy of this WindowOptions.
    /// </summary>
    public WindowOptions Clone()
    {
        return new WindowOptions(this);
    }

    /// <summary>
    /// Explicit interface implementation for ICloneable.
    /// </summary>
    object ICloneable.Clone()
    {
        return Clone();
    }
}

/// <summary>
/// Theme color options.
/// </summary>
public class ThemeOptions : ICloneable
{
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public string? Cursor { get; set; }
    public string? CursorAccent { get; set; }
    public string? Selection { get; set; }
    public string? SelectionInactive { get; set; }

    // Standard colors (0-7)
    public string? Black { get; set; }
    public string? Red { get; set; }
    public string? Green { get; set; }
    public string? Yellow { get; set; }
    public string? Blue { get; set; }
    public string? Magenta { get; set; }
    public string? Cyan { get; set; }
    public string? White { get; set; }

    // Bright colors (8-15)
    public string? BrightBlack { get; set; }
    public string? BrightRed { get; set; }
    public string? BrightGreen { get; set; }
    public string? BrightYellow { get; set; }
    public string? BrightBlue { get; set; }
    public string? BrightMagenta { get; set; }
    public string? BrightCyan { get; set; }
    public string? BrightWhite { get; set; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ThemeOptions()
    {
    }

    /// <summary>
    /// Copy constructor for cloning.
    /// </summary>
    public ThemeOptions(ThemeOptions other)
    {
        Foreground = other.Foreground;
        Background = other.Background;
        Cursor = other.Cursor;
        CursorAccent = other.CursorAccent;
        Selection = other.Selection;
        SelectionInactive = other.SelectionInactive;
        Black = other.Black;
        Red = other.Red;
        Green = other.Green;
        Yellow = other.Yellow;
        Blue = other.Blue;
        Magenta = other.Magenta;
        Cyan = other.Cyan;
        White = other.White;
        BrightBlack = other.BrightBlack;
        BrightRed = other.BrightRed;
        BrightGreen = other.BrightGreen;
        BrightYellow = other.BrightYellow;
        BrightBlue = other.BrightBlue;
        BrightMagenta = other.BrightMagenta;
        BrightCyan = other.BrightCyan;
        BrightWhite = other.BrightWhite;
    }

    /// <summary>
    /// Creates a copy of this ThemeOptions.
    /// </summary>
    public ThemeOptions Clone()
    {
        return new ThemeOptions(this);
    }

    /// <summary>
    /// Explicit interface implementation for ICloneable.
    /// </summary>
    object ICloneable.Clone()
    {
        return Clone();
    }
}

/// <summary>
/// Key event information.
/// </summary>
public class KeyEvent
{
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The PHYSICAL key, named as a browser names ev.code ("KeyA", "Digit5", "Numpad7",
    /// "ShiftLeft"), independent of layout. The Kitty keyboard protocol needs it twice over:
    /// the base-layout key under a shifted or Option-composed character, and which of the two
    /// Shift keys was actually pressed. Empty when the host cannot say.
    /// </summary>
    public string Code { get; set; } = string.Empty;
    public bool CtrlKey { get; set; }
    public bool AltKey { get; set; }
    public bool ShiftKey { get; set; }
    public bool MetaKey { get; set; }
    public int KeyCode { get; set; }
}
