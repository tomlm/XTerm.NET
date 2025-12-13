using XTerm.NET.Common;

namespace XTerm.NET.Options;

/// <summary>
/// Terminal configuration options.
/// </summary>
public class TerminalOptions
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
    /// Clones the options.
    /// </summary>
    public TerminalOptions Clone()
    {
        return new TerminalOptions
        {
            Cols = Cols,
            Rows = Rows,
            Scrollback = Scrollback,
            TabStopWidth = TabStopWidth,
            BellSound = BellSound,
            BellVolume = BellVolume,
            BellStyle = BellStyle,
            CursorBlinkRate = CursorBlinkRate,
            CursorStyle = CursorStyle,
            CursorBlink = CursorBlink,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            FontWeightBold = FontWeightBold,
            LetterSpacing = LetterSpacing,
            LineHeight = LineHeight,
            Wraparound = Wraparound,
            ConvertEol = ConvertEol,
            TermName = TermName,
            FastScrollModifier = FastScrollModifier,
            ScrollSensitivity = ScrollSensitivity,
            AllowTransparency = AllowTransparency,
            MacOptionIsMeta = MacOptionIsMeta,
            RightClickSelectsWord = RightClickSelectsWord,
            RendererType = RendererType,
            WindowOptions = WindowOptions.Clone(),
            Theme = Theme.Clone(),
            MinimumContrastRatio = MinimumContrastRatio,
            DrawBoldTextInBrightColors = DrawBoldTextInBrightColors,
            CustomKeyEventHandler = CustomKeyEventHandler
        };
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
public class WindowOptions
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

    public WindowOptions Clone()
    {
        return new WindowOptions
        {
            GetWinPosition = GetWinPosition,
            GetWinSizePixels = GetWinSizePixels,
            GetWinSizeChars = GetWinSizeChars,
            GetScreenSizePixels = GetScreenSizePixels,
            GetCellSizePixels = GetCellSizePixels,
            GetIconTitle = GetIconTitle,
            GetWinTitle = GetWinTitle,
            GetWinState = GetWinState,
            SetWinPosition = SetWinPosition,
            SetWinSizePixels = SetWinSizePixels,
            SetWinSizeChars = SetWinSizeChars,
            RaiseWin = RaiseWin,
            LowerWin = LowerWin,
            RefreshWin = RefreshWin,
            RestoreWin = RestoreWin,
            MaximizeWin = MaximizeWin,
            MinimizeWin = MinimizeWin,
            FullscreenWin = FullscreenWin
        };
    }
}

/// <summary>
/// Theme color options.
/// </summary>
public class ThemeOptions
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

    public ThemeOptions Clone()
    {
        return new ThemeOptions
        {
            Foreground = Foreground,
            Background = Background,
            Cursor = Cursor,
            CursorAccent = CursorAccent,
            Selection = Selection,
            SelectionInactive = SelectionInactive,
            Black = Black,
            Red = Red,
            Green = Green,
            Yellow = Yellow,
            Blue = Blue,
            Magenta = Magenta,
            Cyan = Cyan,
            White = White,
            BrightBlack = BrightBlack,
            BrightRed = BrightRed,
            BrightGreen = BrightGreen,
            BrightYellow = BrightYellow,
            BrightBlue = BrightBlue,
            BrightMagenta = BrightMagenta,
            BrightCyan = BrightCyan,
            BrightWhite = BrightWhite
        };
    }
}

/// <summary>
/// Key event information.
/// </summary>
public class KeyEvent
{
    public string Key { get; set; } = string.Empty;
    public bool CtrlKey { get; set; }
    public bool AltKey { get; set; }
    public bool ShiftKey { get; set; }
    public bool MetaKey { get; set; }
    public int KeyCode { get; set; }
}
