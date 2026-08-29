using XTerm.Options;
using XTerm.Common;

namespace XTerm.Tests.Options;

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
        Assert.True(options.ClipboardWriteEnabled);
        Assert.False(options.ClipboardReadEnabled);
        Assert.NotNull(options.WindowOptions);
        Assert.NotNull(options.Theme);
        Assert.Equal(1, options.MinimumContrastRatio);
        Assert.True(options.DrawBoldTextInBrightColors);
        Assert.True(options.KittyNotificationsEnabled);   // display-only: on by default, like every terminal that implements OSC 99
        Assert.Null(options.CustomKeyEventHandler);
        Assert.True(options.ClipboardWriteEnabled);
        Assert.False(options.ClipboardReadEnabled);
        Assert.Equal(64 * 1024 * 1024, options.MaxClipboardBytes);
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
            FontFamily = "Test Font",
            ClipboardWriteEnabled = false,
            ClipboardReadEnabled = true
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
        Assert.Equal(options.ClipboardWriteEnabled, clone.ClipboardWriteEnabled);
        Assert.Equal(options.ClipboardReadEnabled, clone.ClipboardReadEnabled);

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
        options.KittyNotificationsEnabled = true;

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
        Assert.True(options.KittyNotificationsEnabled);
    }

    [Fact]
    public void Clone_CopiesEverySettableProperty()
    {
        var options = new TerminalOptions();
        SetDistinctValues(options);

        var clone = options.Clone();

        AssertPropertiesEqual(options, clone);
        Assert.NotSame(options.WindowOptions, clone.WindowOptions);
        Assert.NotSame(options.Theme, clone.Theme);
        AssertPropertiesEqual(options.WindowOptions, clone.WindowOptions);
        AssertPropertiesEqual(options.Theme, clone.Theme);
    }

    private static void SetDistinctValues(object target)
    {
        foreach (var property in target.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            var current = property.GetValue(target);
            object? value = property.PropertyType switch
            {
                var type when type == typeof(bool) => !(bool)current!,
                var type when type == typeof(int) => (int)current! + 1,
                var type when type == typeof(long) => (long)current! + 1,
                var type when type == typeof(double) => (double)current! + 0.5,
                var type when type == typeof(string) => (current as string ?? string.Empty) + "-clone",
                var type when type.IsEnum => Enum.GetValues(type).Cast<object>()
                    .First(value => !Equals(value, current)),
                var type when type == typeof(Func<KeyEvent, bool>) => (Func<KeyEvent, bool>)(_ => true),

                // The nested option objects are varied by the recursion below, not here: replacing
                // the reference would defeat the NotSame checks the caller makes on them.
                var type when type == typeof(WindowOptions) || type == typeof(ThemeOptions) => current,

                // Anything else stops the test rather than passing it. A type this cannot vary gets
                // set to the value it already had, so the clone would be compared default against
                // default and agree whether or not the copy constructor ever touched it -- the
                // guard reporting success at exactly the moment it stopped guarding. The property
                // most likely to be forgotten is the one added next, which is also when a new type
                // is most likely to appear, so the two failures would arrive together and cancel.
                _ => throw new NotSupportedException(
                    $"{target.GetType().Name}.{property.Name} is a {property.PropertyType.Name}; "
                    + "teach SetDistinctValues how to vary it, or this guard silently stops "
                    + "checking that property.")
            };

            property.SetValue(target, value);
        }

        if (target is TerminalOptions options)
        {
            SetDistinctValues(options.WindowOptions);
            SetDistinctValues(options.Theme);
        }
    }

    private static void AssertPropertiesEqual(object expected, object actual)
    {
        foreach (var property in expected.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            if (property.PropertyType is { } type
                && (type == typeof(WindowOptions) || type == typeof(ThemeOptions)))
            {
                continue;
            }

            Assert.Equal(property.GetValue(expected), property.GetValue(actual));
        }
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

/// <summary>
/// Options that a host changes while the terminal is running, rather than at construction.
/// A settable property that quietly does nothing is worse than one that is not there.
/// </summary>
public class LiveOptionsTests
{
    private static Terminal WithHistory(int rows, int scrollback, int linesWritten)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = rows, Scrollback = scrollback });
        for (var i = 0; i < linesWritten; i++)
            terminal.WriteLine($"line{i}");
        return terminal;
    }
    private static bool Holds(Terminal t, string text)
    {
        for (var y = 0; y < t.Buffer.Lines.Length; y++)
            if (t.Buffer.Lines[y]?.TranslateToString(true).Trim() == text)
                return true;
        return false;
    }
    [Fact]
    public void Lowering_the_scrollback_after_construction_shrinks_the_history()
    {
        // Scrollback was read once, when the buffer was built, and never again -- so a host
        // reclaiming memory set a property that reported the new value and changed nothing.
        var terminal = WithHistory(rows: 4, scrollback: 50, linesWritten: 40);
        Assert.Equal(54, terminal.Buffer.Lines.MaxLength);
        terminal.Options.Scrollback = 5;
        Assert.Equal(9, terminal.Buffer.Lines.MaxLength);
    }
    [Fact]
    public void Shrinking_the_scrollback_drops_the_oldest_lines_and_keeps_the_screen()
    {
        // CircularList.Resize keeps the FRONT of the list, which for a scrollback is backwards:
        // it would discard the screen the user is looking at and keep the history nobody asked to
        // keep. The oldest go.
        var terminal = WithHistory(rows: 4, scrollback: 50, linesWritten: 40);
        Assert.True(Holds(terminal, "line0"), "the oldest line should still be here before shrinking");
        terminal.Options.Scrollback = 5;
        Assert.False(Holds(terminal, "line0"), "the oldest line should have been dropped");
        Assert.True(Holds(terminal, "line39"), "the newest line must survive -- it is on screen");
    }
    [Fact]
    public void Shrinking_the_scrollback_leaves_the_viewport_on_the_live_bottom()
    {
        // The viewport is recomputed against what is left rather than shifted by the trim amount,
        // or it ends up a fixed distance from rows that no longer exist and everything written
        // afterwards lands outside the visible area.
        var terminal = WithHistory(rows: 4, scrollback: 50, linesWritten: 40);
        terminal.Options.Scrollback = 5;
        Assert.Equal(terminal.Buffer.YBase, terminal.Buffer.YDisp);
        terminal.WriteLine("after");
        Assert.True(Holds(terminal, "after"));
    }
    [Fact]
    public void Raising_the_scrollback_after_construction_grows_the_history()
    {
        var terminal = WithHistory(rows: 4, scrollback: 5, linesWritten: 20);
        Assert.Equal(9, terminal.Buffer.Lines.MaxLength);
        terminal.Options.Scrollback = 100;
        Assert.Equal(104, terminal.Buffer.Lines.MaxLength);
        Assert.True(Holds(terminal, "line19"), "growing must not disturb what is already held");
    }
    [Fact]
    public void The_alternate_screen_keeps_no_history_whatever_the_scrollback_says()
    {
        // The alternate buffer is constructed with none by definition, and a later write to the
        // option must not give it any -- a full-screen program's scrollback is the shell's.
        var terminal = WithHistory(rows: 4, scrollback: 50, linesWritten: 20);
        terminal.Write($"{((char)0x1B)}[?1049h");
        var altCapacity = terminal.Buffer.Lines.MaxLength;
        terminal.Options.Scrollback = 500;
        Assert.Equal(altCapacity, terminal.Buffer.Lines.MaxLength);
    }
    [Fact]
    public void Setting_the_scrollback_to_what_it_already_is_changes_nothing()
    {
        var terminal = WithHistory(rows: 4, scrollback: 50, linesWritten: 40);
        var before = terminal.Buffer.Lines.MaxLength;
        terminal.Options.Scrollback = 50;
        Assert.Equal(before, terminal.Buffer.Lines.MaxLength);
        Assert.True(Holds(terminal, "line0"), "a no-op write must not trim anything");
    }
    [Fact]
    public void The_options_object_a_caller_kept_does_not_reach_the_terminal()
    {
        // The snapshot contract from #101, restated here because the live hook is installed on the
        // terminal's own copy: making Scrollback live must not quietly re-alias the two.
        var mine = new TerminalOptions { Cols = 20, Rows = 4, Scrollback = 50 };
        var terminal = new Terminal(mine);
        mine.Scrollback = 5;
        Assert.Equal(54, terminal.Buffer.Lines.MaxLength);
    }
}
