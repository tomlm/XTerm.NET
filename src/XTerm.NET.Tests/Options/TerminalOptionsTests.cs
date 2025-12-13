using XTerm.NET.Options;
using XTerm.NET.Common;

namespace XTerm.NET.Tests.Options;

public class TerminalOptionsTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var options = new TerminalOptions();

        // Assert
        Assert.Equal(80, options.Cols);
        Assert.Equal(24, options.Rows);
        Assert.Equal(1000, options.Scrollback);
        Assert.Equal(8, options.TabStopWidth);
        Assert.False(options.BellSound);
        Assert.Equal(0.5, options.BellVolume);
        Assert.Equal(BellStyle.None, options.BellStyle);
        Assert.Equal(530, options.CursorBlinkRate);
        Assert.Equal(CursorStyle.Block, options.CursorStyle);
        Assert.False(options.CursorBlink);
        Assert.Equal("monospace", options.FontFamily);
        Assert.Equal(15, options.FontSize);
        Assert.Equal("normal", options.FontWeight);
        Assert.Equal("bold", options.FontWeightBold);
        Assert.Equal(0, options.LetterSpacing);
        Assert.Equal(1.0, options.LineHeight);
        Assert.True(options.Wraparound);
        Assert.False(options.ConvertEol);
        Assert.Equal("xterm", options.TermName);
        Assert.False(options.FastScrollModifier);
        Assert.Equal(1, options.ScrollSensitivity);
        Assert.False(options.AllowTransparency);
        Assert.False(options.MacOptionIsMeta);
        Assert.True(options.RightClickSelectsWord);
        Assert.Equal(RendererType.Canvas, options.RendererType);
        Assert.NotNull(options.WindowOptions);
        Assert.NotNull(options.Theme);
        Assert.Equal(1, options.MinimumContrastRatio);
        Assert.True(options.DrawBoldTextInBrightColors);
        Assert.Null(options.CustomKeyEventHandler);
    }

    [Fact]
    public void Cols_CanBeSet()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.Cols = 120;

        // Assert
        Assert.Equal(120, options.Cols);
    }

    [Fact]
    public void Rows_CanBeSet()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.Rows = 40;

        // Assert
        Assert.Equal(40, options.Rows);
    }

    [Fact]
    public void Scrollback_CanBeSet()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.Scrollback = 5000;

        // Assert
        Assert.Equal(5000, options.Scrollback);
    }

    [Fact]
    public void BellSound_CanBeToggled()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.BellSound = true;

        // Assert
        Assert.True(options.BellSound);
    }

    [Fact]
    public void CursorStyle_CanBeChanged()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.CursorStyle = CursorStyle.Bar;

        // Assert
        Assert.Equal(CursorStyle.Bar, options.CursorStyle);
    }

    [Fact]
    public void CursorBlink_CanBeToggled()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.CursorBlink = true;

        // Assert
        Assert.True(options.CursorBlink);
    }

    [Fact]
    public void FontFamily_CanBeSet()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.FontFamily = "Courier New";

        // Assert
        Assert.Equal("Courier New", options.FontFamily);
    }

    [Fact]
    public void FontSize_CanBeSet()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.FontSize = 20;

        // Assert
        Assert.Equal(20, options.FontSize);
    }

    [Fact]
    public void Wraparound_CanBeToggled()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.Wraparound = false;

        // Assert
        Assert.False(options.Wraparound);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var options = new TerminalOptions
        {
            Cols = 100,
            Rows = 30,
            Scrollback = 2000,
            BellSound = true,
            CursorBlink = true,
            FontFamily = "Test Font"
        };

        // Act
        var clone = options.Clone();

        // Assert
        Assert.Equal(options.Cols, clone.Cols);
        Assert.Equal(options.Rows, clone.Rows);
        Assert.Equal(options.Scrollback, clone.Scrollback);
        Assert.Equal(options.BellSound, clone.BellSound);
        Assert.Equal(options.CursorBlink, clone.CursorBlink);
        Assert.Equal(options.FontFamily, clone.FontFamily);

        // Verify independence
        clone.Cols = 120;
        Assert.Equal(100, options.Cols);
        Assert.Equal(120, clone.Cols);
    }

    [Fact]
    public void CustomKeyEventHandler_CanBeSet()
    {
        // Arrange
        var options = new TerminalOptions();
        Func<KeyEvent, bool> handler = (e) => true;

        // Act
        options.CustomKeyEventHandler = handler;

        // Assert
        Assert.NotNull(options.CustomKeyEventHandler);
        Assert.Equal(handler, options.CustomKeyEventHandler);
    }

    [Theory]
    [InlineData(BellStyle.None)]
    [InlineData(BellStyle.Sound)]
    [InlineData(BellStyle.Visual)]
    [InlineData(BellStyle.Both)]
    public void BellStyle_CanBeSet(BellStyle style)
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.BellStyle = style;

        // Assert
        Assert.Equal(style, options.BellStyle);
    }

    [Theory]
    [InlineData(RendererType.Canvas)]
    [InlineData(RendererType.Dom)]
    [InlineData(RendererType.WebGL)]
    public void RendererType_CanBeSet(RendererType type)
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.RendererType = type;

        // Assert
        Assert.Equal(type, options.RendererType);
    }

    [Fact]
    public void AllProperties_CanBeModified()
    {
        // Arrange
        var options = new TerminalOptions();

        // Act
        options.Cols = 100;
        options.Rows = 30;
        options.Scrollback = 2000;
        options.TabStopWidth = 4;
        options.BellSound = true;
        options.BellVolume = 0.8;
        options.BellStyle = BellStyle.Both;
        options.CursorBlinkRate = 600;
        options.CursorStyle = CursorStyle.Underline;
        options.CursorBlink = true;
        options.FontFamily = "Arial";
        options.FontSize = 18;
        options.FontWeight = "600";
        options.FontWeightBold = "800";
        options.LetterSpacing = 1.5;
        options.LineHeight = 1.2;
        options.Wraparound = false;
        options.ConvertEol = true;
        options.TermName = "xterm-256color";
        options.FastScrollModifier = true;
        options.ScrollSensitivity = 3;
        options.AllowTransparency = true;
        options.MacOptionIsMeta = true;
        options.RightClickSelectsWord = false;
        options.RendererType = RendererType.WebGL;
        options.MinimumContrastRatio = 4.5;
        options.DrawBoldTextInBrightColors = false;

        // Assert
        Assert.Equal(100, options.Cols);
        Assert.Equal(30, options.Rows);
        Assert.Equal(2000, options.Scrollback);
        Assert.Equal(4, options.TabStopWidth);
        Assert.True(options.BellSound);
        Assert.Equal(0.8, options.BellVolume);
        Assert.Equal(BellStyle.Both, options.BellStyle);
        Assert.Equal(600, options.CursorBlinkRate);
        Assert.Equal(CursorStyle.Underline, options.CursorStyle);
        Assert.True(options.CursorBlink);
        Assert.Equal("Arial", options.FontFamily);
        Assert.Equal(18, options.FontSize);
        Assert.Equal("600", options.FontWeight);
        Assert.Equal("800", options.FontWeightBold);
        Assert.Equal(1.5, options.LetterSpacing);
        Assert.Equal(1.2, options.LineHeight);
        Assert.False(options.Wraparound);
        Assert.True(options.ConvertEol);
        Assert.Equal("xterm-256color", options.TermName);
        Assert.True(options.FastScrollModifier);
        Assert.Equal(3, options.ScrollSensitivity);
        Assert.True(options.AllowTransparency);
        Assert.True(options.MacOptionIsMeta);
        Assert.False(options.RightClickSelectsWord);
        Assert.Equal(RendererType.WebGL, options.RendererType);
        Assert.Equal(4.5, options.MinimumContrastRatio);
        Assert.False(options.DrawBoldTextInBrightColors);
    }
}

public class WindowOptionsTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var options = new WindowOptions();

        // Assert
        Assert.False(options.GetWinPosition);
        Assert.False(options.GetWinSizePixels);
        Assert.False(options.GetWinSizeChars);
        Assert.False(options.GetScreenSizePixels);
        Assert.False(options.GetCellSizePixels);
        Assert.False(options.GetIconTitle);
        Assert.False(options.GetWinTitle);
        Assert.False(options.GetWinState);
        Assert.False(options.SetWinPosition);
        Assert.False(options.SetWinSizePixels);
        Assert.False(options.SetWinSizeChars);
        Assert.False(options.RaiseWin);
        Assert.False(options.LowerWin);
        Assert.False(options.RefreshWin);
        Assert.False(options.RestoreWin);
        Assert.False(options.MaximizeWin);
        Assert.False(options.MinimizeWin);
        Assert.False(options.FullscreenWin);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var options = new WindowOptions
        {
            GetWinPosition = true,
            SetWinPosition = true,
            MaximizeWin = true
        };

        // Act
        var clone = options.Clone();

        // Assert
        Assert.Equal(options.GetWinPosition, clone.GetWinPosition);
        Assert.Equal(options.SetWinPosition, clone.SetWinPosition);
        Assert.Equal(options.MaximizeWin, clone.MaximizeWin);

        // Verify independence
        clone.GetWinPosition = false;
        Assert.True(options.GetWinPosition);
        Assert.False(clone.GetWinPosition);
    }

    [Fact]
    public void AllProperties_CanBeToggled()
    {
        // Arrange
        var options = new WindowOptions();

        // Act
        options.GetWinPosition = true;
        options.GetWinSizePixels = true;
        options.GetWinSizeChars = true;
        options.GetScreenSizePixels = true;
        options.GetCellSizePixels = true;
        options.GetIconTitle = true;
        options.GetWinTitle = true;
        options.GetWinState = true;
        options.SetWinPosition = true;
        options.SetWinSizePixels = true;
        options.SetWinSizeChars = true;
        options.RaiseWin = true;
        options.LowerWin = true;
        options.RefreshWin = true;
        options.RestoreWin = true;
        options.MaximizeWin = true;
        options.MinimizeWin = true;
        options.FullscreenWin = true;

        // Assert
        Assert.True(options.GetWinPosition);
        Assert.True(options.GetWinSizePixels);
        Assert.True(options.GetWinSizeChars);
        Assert.True(options.GetScreenSizePixels);
        Assert.True(options.GetCellSizePixels);
        Assert.True(options.GetIconTitle);
        Assert.True(options.GetWinTitle);
        Assert.True(options.GetWinState);
        Assert.True(options.SetWinPosition);
        Assert.True(options.SetWinSizePixels);
        Assert.True(options.SetWinSizeChars);
        Assert.True(options.RaiseWin);
        Assert.True(options.LowerWin);
        Assert.True(options.RefreshWin);
        Assert.True(options.RestoreWin);
        Assert.True(options.MaximizeWin);
        Assert.True(options.MinimizeWin);
        Assert.True(options.FullscreenWin);
    }
}

public class ThemeOptionsTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var theme = new ThemeOptions();

        // Assert
        Assert.Null(theme.Foreground);
        Assert.Null(theme.Background);
        Assert.Null(theme.Cursor);
        Assert.Null(theme.CursorAccent);
        Assert.Null(theme.Selection);
        Assert.Null(theme.SelectionInactive);
        Assert.Null(theme.Black);
        Assert.Null(theme.Red);
        Assert.Null(theme.Green);
        Assert.Null(theme.Yellow);
        Assert.Null(theme.Blue);
        Assert.Null(theme.Magenta);
        Assert.Null(theme.Cyan);
        Assert.Null(theme.White);
        Assert.Null(theme.BrightBlack);
        Assert.Null(theme.BrightRed);
        Assert.Null(theme.BrightGreen);
        Assert.Null(theme.BrightYellow);
        Assert.Null(theme.BrightBlue);
        Assert.Null(theme.BrightMagenta);
        Assert.Null(theme.BrightCyan);
        Assert.Null(theme.BrightWhite);
    }

    [Fact]
    public void Colors_CanBeSet()
    {
        // Arrange
        var theme = new ThemeOptions();

        // Act
        theme.Foreground = "#FFFFFF";
        theme.Background = "#000000";
        theme.Red = "#FF0000";
        theme.Green = "#00FF00";
        theme.Blue = "#0000FF";

        // Assert
        Assert.Equal("#FFFFFF", theme.Foreground);
        Assert.Equal("#000000", theme.Background);
        Assert.Equal("#FF0000", theme.Red);
        Assert.Equal("#00FF00", theme.Green);
        Assert.Equal("#0000FF", theme.Blue);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var theme = new ThemeOptions
        {
            Foreground = "#FFFFFF",
            Background = "#000000",
            Red = "#FF0000",
            BrightRed = "#FF6666"
        };

        // Act
        var clone = theme.Clone();

        // Assert
        Assert.Equal(theme.Foreground, clone.Foreground);
        Assert.Equal(theme.Background, clone.Background);
        Assert.Equal(theme.Red, clone.Red);
        Assert.Equal(theme.BrightRed, clone.BrightRed);

        // Verify independence
        clone.Foreground = "#AAAAAA";
        Assert.Equal("#FFFFFF", theme.Foreground);
        Assert.Equal("#AAAAAA", clone.Foreground);
    }

    [Fact]
    public void AllColors_CanBeSet()
    {
        // Arrange
        var theme = new ThemeOptions();

        // Act
        theme.Foreground = "#F";
        theme.Background = "#B";
        theme.Cursor = "#C";
        theme.CursorAccent = "#CA";
        theme.Selection = "#S";
        theme.SelectionInactive = "#SI";
        theme.Black = "#0";
        theme.Red = "#1";
        theme.Green = "#2";
        theme.Yellow = "#3";
        theme.Blue = "#4";
        theme.Magenta = "#5";
        theme.Cyan = "#6";
        theme.White = "#7";
        theme.BrightBlack = "#8";
        theme.BrightRed = "#9";
        theme.BrightGreen = "#A";
        theme.BrightYellow = "#BB";
        theme.BrightBlue = "#CC";
        theme.BrightMagenta = "#DD";
        theme.BrightCyan = "#EE";
        theme.BrightWhite = "#FF";

        // Assert
        Assert.Equal("#F", theme.Foreground);
        Assert.Equal("#B", theme.Background);
        Assert.Equal("#C", theme.Cursor);
        Assert.Equal("#CA", theme.CursorAccent);
        Assert.Equal("#S", theme.Selection);
        Assert.Equal("#SI", theme.SelectionInactive);
        Assert.Equal("#0", theme.Black);
        Assert.Equal("#1", theme.Red);
        Assert.Equal("#2", theme.Green);
        Assert.Equal("#3", theme.Yellow);
        Assert.Equal("#4", theme.Blue);
        Assert.Equal("#5", theme.Magenta);
        Assert.Equal("#6", theme.Cyan);
        Assert.Equal("#7", theme.White);
        Assert.Equal("#8", theme.BrightBlack);
        Assert.Equal("#9", theme.BrightRed);
        Assert.Equal("#A", theme.BrightGreen);
        Assert.Equal("#BB", theme.BrightYellow);
        Assert.Equal("#CC", theme.BrightBlue);
        Assert.Equal("#DD", theme.BrightMagenta);
        Assert.Equal("#EE", theme.BrightCyan);
        Assert.Equal("#FF", theme.BrightWhite);
    }
}

public class KeyEventTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        var keyEvent = new KeyEvent();

        // Assert
        Assert.Equal(string.Empty, keyEvent.Key);
        Assert.False(keyEvent.CtrlKey);
        Assert.False(keyEvent.AltKey);
        Assert.False(keyEvent.ShiftKey);
        Assert.False(keyEvent.MetaKey);
        Assert.Equal(0, keyEvent.KeyCode);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        var keyEvent = new KeyEvent();

        // Act
        keyEvent.Key = "Enter";
        keyEvent.CtrlKey = true;
        keyEvent.AltKey = true;
        keyEvent.ShiftKey = true;
        keyEvent.MetaKey = true;
        keyEvent.KeyCode = 13;

        // Assert
        Assert.Equal("Enter", keyEvent.Key);
        Assert.True(keyEvent.CtrlKey);
        Assert.True(keyEvent.AltKey);
        Assert.True(keyEvent.ShiftKey);
        Assert.True(keyEvent.MetaKey);
        Assert.Equal(13, keyEvent.KeyCode);
    }
}
