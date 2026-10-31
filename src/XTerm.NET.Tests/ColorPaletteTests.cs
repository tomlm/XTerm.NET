using System.Threading;
using System.Threading.Tasks;
using XTerm;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Covers OSC 4 (indexed palette), OSC 10/11/12 (foreground, background, cursor), and the
/// OSC 104/110/111/112 resets.
/// </summary>
public class ColorPaletteTests
{
    private Terminal CreateTerminal(ThemeOptions? theme = null)
    {
        var options = new TerminalOptions { Cols = 80, Rows = 24 };
        if (theme is not null)
        {
            options.Theme = theme;
        }

        return new Terminal(options);
    }

    private static List<string> CaptureReplies(Terminal terminal)
    {
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return replies;
    }

    // ---- defaults ----------------------------------------------------------------------------

    [Fact]
    public void Defaults_MatchXtermAnsiColors()
    {
        var terminal = CreateTerminal();

        Assert.Equal(0x000000, terminal.Colors[0]);
        Assert.Equal(0xCD0000, terminal.Colors[1]);
        Assert.Equal(0xFFFFFF, terminal.Colors[15]);
    }

    [Fact]
    public void Defaults_ComputeThe216ColorCube()
    {
        var terminal = CreateTerminal();

        Assert.Equal(0x000000, terminal.Colors[16]);   // cube origin
        Assert.Equal(0xFFFFFF, terminal.Colors[231]);  // cube far corner
        Assert.Equal(0x005FAF, terminal.Colors[25]);   // r=0 g=1 b=3 -> 0,95,175
    }

    [Fact]
    public void Defaults_ComputeTheGrayscaleRamp()
    {
        var terminal = CreateTerminal();

        Assert.Equal(0x080808, terminal.Colors[232]);
        Assert.Equal(0xEEEEEE, terminal.Colors[255]);
    }

    [Fact]
    public void Defaults_PreserveThePreviousQueryAnswers_WhenNoThemeIsSet()
    {
        // This change must not move colours for an embedder that sets no theme; it only stops the
        // answers being constants.
        var terminal = CreateTerminal();

        Assert.Equal(0xFFFFFF, terminal.Colors.Foreground);
        Assert.Equal(0x000000, terminal.Colors.Background);
        Assert.Equal(0xFFFFFF, terminal.Colors.Cursor);
    }

    // ---- the light background case ------------------------------------------------------------

    [Fact]
    public void Theme_SetsTheBackground_AndTheQueryReportsIt()
    {
        // The reason this PR exists. A program asks what the background is before choosing its own
        // colours; a constant reply of black made every one of them render for a dark terminal.
        var terminal = CreateTerminal(new ThemeOptions { Background = "#ffffff", Foreground = "#000000" });
        var replies = CaptureReplies(terminal);

        terminal.Write("\x1B]11;?\x07");

        Assert.Equal("\u001b]11;rgb:ffff/ffff/ffff\u0007", Assert.Single(replies));
    }

    [Fact]
    public void IsLightBackground_FollowsTheTheme()
    {
        Assert.True(CreateTerminal(new ThemeOptions { Background = "#ffffff" }).Colors.IsLightBackground);
        Assert.False(CreateTerminal(new ThemeOptions { Background = "#000000" }).Colors.IsLightBackground);

        // Luma-weighted rather than averaged: pure blue is dark despite a high channel value.
        Assert.False(CreateTerminal(new ThemeOptions { Background = "#0000ff" }).Colors.IsLightBackground);
    }

    [Fact]
    public void ApplyTheme_ReseedsAtRuntime_ForAnOsThemeFlip()
    {
        var terminal = CreateTerminal(new ThemeOptions { Background = "#000000" });
        Assert.False(terminal.Colors.IsLightBackground);

        terminal.Colors.ApplyTheme(new ThemeOptions { Background = "#ffffff", Foreground = "#000000" });

        Assert.True(terminal.Colors.IsLightBackground);
        Assert.Equal(0x000000, terminal.Colors.Foreground);
    }

    [Fact]
    public void Reset_RestoresTheEmbedderTheme_NotAFactoryDarkPalette()
    {
        // The failure this guards: a program sets colours, then resets, and a light terminal is
        // left black because "reset" meant xterm's defaults rather than the configured theme.
        var terminal = CreateTerminal(new ThemeOptions { Background = "#ffffff", Black = "#eeeeee" });

        terminal.Write("\x1B]11;rgb:00/00/00\x07");
        terminal.Write("\x1B]4;0;#123456\x07");
        Assert.Equal(0x000000, terminal.Colors.Background);

        terminal.Write("\x1B]111\x07");
        terminal.Write("\x1B]104\x07");

        Assert.Equal(0xFFFFFF, terminal.Colors.Background);
        Assert.Equal(0xEEEEEE, terminal.Colors[0]);
    }

    [Fact]
    public void Theme_OverridesAnsiSlots()
    {
        var terminal = CreateTerminal(new ThemeOptions { Red = "#ff8800", BrightWhite = "rgb:11/22/33" });

        Assert.Equal(0xFF8800, terminal.Colors[1]);
        Assert.Equal(0x112233, terminal.Colors[15]);
    }

    // ---- OSC 4 -------------------------------------------------------------------------------

    [Fact]
    public void Osc4_SetsAnIndexedColor()
    {
        var terminal = CreateTerminal();

        terminal.Write("\x1B]4;1;rgb:ff/00/00\x07");

        Assert.Equal(0xFF0000, terminal.Colors[1]);
    }

    [Fact]
    public void Osc4_SetsMultiplePairsInOneSequence()
    {
        // Theme scripts send all sixteen at once rather than as sixteen sequences.
        var terminal = CreateTerminal();

        terminal.Write("\x1B]4;1;#ff0000;2;#00ff00;3;#0000ff\x07");

        Assert.Equal(0xFF0000, terminal.Colors[1]);
        Assert.Equal(0x00FF00, terminal.Colors[2]);
        Assert.Equal(0x0000FF, terminal.Colors[3]);
    }

    [Fact]
    public void Osc4_QueriesAnIndexedColor()
    {
        var terminal = CreateTerminal();
        var replies = CaptureReplies(terminal);

        terminal.Write("\x1B]4;1;?\x07");

        Assert.Equal("\u001b]4;1;rgb:cdcd/0000/0000\u0007", Assert.Single(replies));
    }

    [Fact]
    public void Osc4_QueryReflectsAPriorSet()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]4;5;#010203\x07");
        var replies = CaptureReplies(terminal);

        terminal.Write("\x1B]4;5;?\x07");

        Assert.Equal("\u001b]4;5;rgb:0101/0202/0303\u0007", Assert.Single(replies));
    }

    [Fact]
    public void Osc4_IgnoresOutOfRangeAndMalformedEntries()
    {
        var terminal = CreateTerminal();

        terminal.Write("\x1B]4;999;#ffffff\x07");
        terminal.Write("\x1B]4;1;notacolor\x07");

        Assert.Equal(0xCD0000, terminal.Colors[1]);
    }

    [Fact]
    public void Osc104_ResetsASingleIndex()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]4;1;#ffffff\x07");
        terminal.Write("\x1B]4;2;#ffffff\x07");

        terminal.Write("\x1B]104;1\x07");

        Assert.Equal(0xCD0000, terminal.Colors[1]);
        Assert.Equal(0xFFFFFF, terminal.Colors[2]);
    }

    // ---- OSC 10/11/12 ------------------------------------------------------------------------

    [Fact]
    public void Osc10_SetsForeground()
    {
        var terminal = CreateTerminal();

        terminal.Write("\x1B]10;#abcdef\x07");

        Assert.Equal(0xABCDEF, terminal.Colors.Foreground);
    }

    [Fact]
    public void Osc10_WithMultipleSpecs_AdvancesThroughResources()
    {
        // xterm defines OSC 10 ; fg ; bg as setting both; handling only the first drops the
        // background silently.
        var terminal = CreateTerminal();

        terminal.Write("\x1B]10;#111111;#222222;#333333\x07");

        Assert.Equal(0x111111, terminal.Colors.Foreground);
        Assert.Equal(0x222222, terminal.Colors.Background);
        Assert.Equal(0x333333, terminal.Colors.Cursor);
    }

    [Fact]
    public void Osc12_SetsCursor()
    {
        var terminal = CreateTerminal();

        terminal.Write("\x1B]12;red\x07");

        Assert.Equal(0xFF0000, terminal.Colors.Cursor);
    }

    [Theory]
    [InlineData("110", 0xFFFFFF)]
    [InlineData("111", 0x000000)]
    [InlineData("112", 0xFFFFFF)]
    public void Osc110To112_ResetTheirOwnResource(string code, int expected)
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]10;#123456\x07");
        terminal.Write("\x1B]11;#123456\x07");
        terminal.Write("\x1B]12;#123456\x07");

        terminal.Write($"\x1B]{code}\x07");

        var actual = code switch
        {
            "110" => terminal.Colors.Foreground,
            "111" => terminal.Colors.Background,
            _ => terminal.Colors.Cursor,
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ColorChanged_FiresForSetsAndNotForNoOps()
    {
        var terminal = CreateTerminal();
        var changes = new List<ColorChangedEventArgs>();
        terminal.Colors.ColorChanged += (_, e) => changes.Add(e);

        terminal.Write("\x1B]4;1;#ff0000\x07");
        terminal.Write("\x1B]4;1;#ff0000\x07");   // same value, no repaint needed

        var change = Assert.Single(changes);
        Assert.Equal(ColorTarget.Indexed, change.Target);
        Assert.Equal(1, change.Index);
        Assert.Equal(0xFF0000, change.Rgb);
    }

    // ---- no-op suppression and index handling ------------------------------------------------

    [Fact]
    public void ResetAllColors_IsSilent_WhenNothingHasChanged()
    {
        // Every other setter here suppresses a no-op. A bare OSC 104 on an untouched palette used to
        // fire anyway, telling a renderer to repaint for a change that did not happen.
        var terminal = CreateTerminal();
        var changes = new List<ColorChangedEventArgs>();
        terminal.Colors.ColorChanged += (_, e) => changes.Add(e);

        terminal.Write("\u001b]104\u0007");

        Assert.Empty(changes);
    }

    [Fact]
    public void ResetAllColors_StillFires_WhenSomethingHadChanged()
    {
        var terminal = CreateTerminal();
        terminal.Write("\u001b]4;1;#ff0000\u0007");

        var changes = new List<ColorChangedEventArgs>();
        terminal.Colors.ColorChanged += (_, e) => changes.Add(e);

        terminal.Write("\u001b]104\u0007");

        var change = Assert.Single(changes);
        Assert.Equal(ColorTarget.Indexed, change.Target);
        Assert.Equal(0xCD0000, terminal.Colors[1]);
    }

    [Fact]
    public void ApplyTheme_IsSilent_WhenTheThemeIsUnchanged()
    {
        var terminal = CreateTerminal(new ThemeOptions { Background = "#ffffff" });
        var changes = new List<ColorChangedEventArgs>();
        terminal.Colors.ColorChanged += (_, e) => changes.Add(e);

        terminal.Colors.ApplyTheme(new ThemeOptions { Background = "#ffffff" });

        Assert.Empty(changes);
    }

    [Fact]
    public void ApplyTheme_FiresOnceForARealChange()
    {
        var terminal = CreateTerminal(new ThemeOptions { Background = "#000000" });
        var changes = new List<ColorChangedEventArgs>();
        terminal.Colors.ColorChanged += (_, e) => changes.Add(e);

        terminal.Colors.ApplyTheme(new ThemeOptions { Background = "#ffffff" });

        Assert.Equal(ColorTarget.All, Assert.Single(changes).Target);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    [InlineData(999)]
    public void PaletteIndex_OutOfRange_Throws(int index)
    {
        // Not clamped. Clamping made SetColor(999, ...) quietly rewrite entry 255 and the indexer
        // answer for entry 0 when asked about -1: a plausible wrong answer where there should have
        // been none.
        var terminal = CreateTerminal();

        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Colors[index]);
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Colors.SetColor(index, 0x123456));
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Colors.ResetColor(index));
    }

    [Fact]
    public void PaletteIndex_OutOfRangeOverOsc_IsStillIgnoredRatherThanThrown()
    {
        // The parser must not surface a malformed sequence as an exception; InputHandler range-checks
        // before it reaches the palette, and that has to keep working now the palette throws.
        var terminal = CreateTerminal();

        var ex = Record.Exception(() => terminal.Write("\u001b]4;999;#ffffff\u0007"));

        Assert.Null(ex);
        Assert.Equal(0xEEEEEE, terminal.Colors[255]);
    }

    // ---- concurrency -------------------------------------------------------------------------

    [Fact]
    public async Task ApplyTheme_IsNeverObservedHalfApplied()
    {
        // The bug this pins: ApplyTheme used to Array.Copy into the live array, and Array.Copy is not
        // atomic, so a renderer scanning the palette could paint a frame that was half one theme and
        // half the other. A reference swap has no middle.
        var dark = new ThemeOptions
        {
            Black = "#000000", Red = "#110000", Green = "#001100", Yellow = "#111100",
            Blue = "#000011", Magenta = "#110011", Cyan = "#001111", White = "#111111",
        };
        var light = new ThemeOptions
        {
            Black = "#ffffff", Red = "#ff0000", Green = "#00ff00", Yellow = "#ffff00",
            Blue = "#0000ff", Magenta = "#ff00ff", Cyan = "#00ffff", White = "#ffffff",
        };

        var terminal = CreateTerminal(dark);
        int[] darkAnsi = ReadAnsi(terminal);
        terminal.Colors.ApplyTheme(light);
        int[] lightAnsi = ReadAnsi(terminal);
        Assert.NotEqual(darkAnsi, lightAnsi);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var mixed = 0;

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                int[] seen = ReadAnsi(terminal);
                if (!seen.SequenceEqual(darkAnsi) && !seen.SequenceEqual(lightAnsi))
                {
                    Interlocked.Increment(ref mixed);
                }
            }
        });

        var writer = Task.Run(() =>
        {
            var toggle = false;
            while (!stop.IsCancellationRequested)
            {
                terminal.Colors.ApplyTheme(toggle ? dark : light);
                toggle = !toggle;
            }
        });

        await Task.WhenAll(reader, writer);

        Assert.Equal(0, Volatile.Read(ref mixed));
    }

    /// <summary>
    /// Reads the first eight ANSI colours through ONE snapshot.
    /// </summary>
    /// <remarks>
    /// Eight calls to the indexer would each take their own snapshot and could straddle a theme
    /// change, which is the whole reason Take exists.
    /// </remarks>
    private static int[] ReadAnsi(Terminal terminal)
    {
        ColorSnapshot snapshot = terminal.Colors.Take();
        var values = new int[8];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = snapshot[i];
        }

        return values;
    }

    [Fact]
    public void Take_ReturnsAViewThatDoesNotMoveAfterwards()
    {
        // The property that lets a renderer trust a snapshot for a whole frame. Writes copy on
        // write for exactly this; mutating the live array in place would have been safe for the
        // VALUE and still broken this.
        var terminal = CreateTerminal();
        ColorSnapshot before = terminal.Colors.Take();
        int original = before[1];

        terminal.Write("\u001b]4;1;#123456\u0007");

        Assert.Equal(original, before[1]);
        Assert.Equal(0x123456, terminal.Colors[1]);
        Assert.Equal(0x123456, terminal.Colors.Take()[1]);
    }

    // ---- colour spec parsing -----------------------------------------------------------------

    [Theory]
    [InlineData("rgb:ff/00/00", 0xFF0000)]
    [InlineData("rgb:f/0/0", 0xFF0000)]              // 1 digit: f is FULL intensity, not 0x0f
    [InlineData("rgb:ffff/0000/0000", 0xFF0000)]     // 4 digits, as emitted by queries
    [InlineData("#ff0000", 0xFF0000)]
    [InlineData("#f00", 0xF00000)]   // X11 left-justifies hash digits: f -> f0, not ff
    [InlineData("#ffff00000000", 0xFF0000)]
    [InlineData("red", 0xFF0000)]
    [InlineData("RED", 0xFF0000)]
    public void ColorSpec_ParsesTheFormsProgramsActuallySend(string spec, int expected)
    {
        Assert.True(ColorSpec.TryParse(spec, out var rgb));
        Assert.Equal(expected, rgb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("rgb:ff/00")]
    [InlineData("rgb:gg/00/00")]
    [InlineData("#ff00")]
    [InlineData("chartreuse")]
    public void ColorSpec_RejectsWhatItCannotRead(string? spec)
    {
        Assert.False(ColorSpec.TryParse(spec, out _));
    }

    [Fact]
    public void ColorSpec_FormatWidensChannelsByRepetition()
    {
        // 0xff must become 0xffff, not 0xff00: full intensity has to survive the widening.
        Assert.Equal("rgb:ffff/0000/8080", ColorSpec.Format(0xFF0080));
    }

    [Fact]
    public void ColorSpec_RoundTripsThroughFormatAndParse()
    {
        Assert.True(ColorSpec.TryParse(ColorSpec.Format(0x123456), out var rgb));
        Assert.Equal(0x123456, rgb);
    }
    // ---- Special colors (OSC 4 index 256+, OSC 5) --------------------------------------------

    [Fact]
    public void Osc5_SetsAndReportsASpecialColor()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]5;0;#112233\x07");
        var replies = CaptureReplies(terminal);
        terminal.Write("\x1B]5;0;?\x07");
        Assert.Equal("\u001b]5;0;rgb:1111/2222/3333\u0007", Assert.Single(replies));
    }

    [Fact]
    public void Osc4_Index257_IsTheSameSlotAsOsc5_Index1()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]4;257;#445566\x07");
        var replies = CaptureReplies(terminal);
        terminal.Write("\x1B]5;1;?\x07");
        Assert.Equal("\u001b]5;1;rgb:4444/5555/6666\u0007", Assert.Single(replies));
    }

    [Fact]
    public void SpecialColor_DefaultsToTheForeground()
    {
        var terminal = CreateTerminal(new ThemeOptions { Foreground = "#010203" });
        var replies = CaptureReplies(terminal);
        terminal.Write("\x1B]5;2;?\x07");
        Assert.Equal("\u001b]5;2;rgb:0101/0202/0303\u0007", Assert.Single(replies));
    }

    [Fact]
    public void Osc105_And_BareOsc104_ResetSpecialColors()
    {
        var terminal = CreateTerminal(new ThemeOptions { Foreground = "#010203" });
        terminal.Write("\x1B]5;0;#ffffff\x07");
        terminal.Write("\x1B]105;0\x07");
        terminal.Write("\x1B]5;1;#ffffff\x07");
        terminal.Write("\x1B]104\x07");            // bare 104 clears the specials too, like xterm
        var replies = CaptureReplies(terminal);
        terminal.Write("\x1B]5;0;?\x07");
        terminal.Write("\x1B]5;1;?\x07");
        Assert.Equal(2, replies.Count);
        Assert.All(replies, r => Assert.EndsWith("rgb:0101/0202/0303\u0007", r));
    }

    [Fact]
    public void Osc5_BoldSlot_DefaultsToTheThemeBoldColor_AndAProgramsSetWins()
    {
        var terminal = CreateTerminal(new ThemeOptions { Foreground = "#010203", Bold = "#a0b0c0" });
        var replies = CaptureReplies(terminal);
        terminal.Write("\x1B]5;0;?\x07");
        terminal.Write("\x1B]5;1;?\x07");          // the other specials still default to the foreground
        terminal.Write("\x1B]5;0;#112233\x07");
        terminal.Write("\x1B]5;0;?\x07");
        terminal.Write("\x1B]105;0\x07");
        terminal.Write("\x1B]5;0;?\x07");
        Assert.Equal(
            new[]
            {
                "\u001b]5;0;rgb:a0a0/b0b0/c0c0\u0007",
                "\u001b]5;1;rgb:0101/0202/0303\u0007",
                "\u001b]5;0;rgb:1111/2222/3333\u0007",
                "\u001b]5;0;rgb:a0a0/b0b0/c0c0\u0007",
            },
            replies);
    }

    // ---- Theme bold colour and foreground resolution -----------------------------------------

    private static AttributeData Attr(int fg, int mode = 0, bool bold = false)
    {
        var attr = AttributeData.Default;
        attr.SetFgColor(fg, mode);
        attr.SetBold(bold);
        return attr;
    }

    private const int DefaultFg = 256;

    [Fact]
    public void ThemeBold_IsParsedLikeTheOtherThemeColors()
    {
        Assert.Equal(0xFFAA00, new ColorPalette(new ThemeOptions { Bold = "#ffaa00" }).Bold);
        Assert.Null(new ColorPalette(new ThemeOptions()).Bold);
        Assert.Null(new ColorPalette(new ThemeOptions { Bold = "not a colour" }).Bold);
        Assert.Null(new ColorPalette().Take().Bold);
    }

    [Fact]
    public void ResolveForeground_BoldWithDefaultForeground_TakesTheBoldColor()
    {
        var snapshot = new ColorPalette(new ThemeOptions { Foreground = "#010203", Bold = "#ffaa00" }).Take();

        Assert.Equal(0xFFAA00, snapshot.ResolveForeground(Attr(DefaultFg, bold: true), true));
        Assert.Equal(0xFFAA00, snapshot.ResolveForeground(Attr(DefaultFg, bold: true), false));
    }

    [Fact]
    public void ResolveForeground_BoldWithAPaletteOrRgbForeground_IgnoresTheBoldColor()
    {
        var snapshot = new ColorPalette(new ThemeOptions { Bold = "#ffaa00" }).Take();

        // Palette 0-7 follows DrawBoldTextInBrightColors, and only that.
        Assert.Equal(snapshot[9], snapshot.ResolveForeground(Attr(1, bold: true), true));
        Assert.Equal(snapshot[1], snapshot.ResolveForeground(Attr(1, bold: true), false));
        // An index past the sixteen has no bright counterpart.
        Assert.Equal(snapshot[100], snapshot.ResolveForeground(Attr(100, bold: true), true));
        Assert.Equal(snapshot[12], snapshot.ResolveForeground(Attr(12, bold: true), true));
        // 24-bit colour comes through exactly as the program named it, even one below 8.
        Assert.Equal(0x123456, snapshot.ResolveForeground(Attr(0x123456, mode: 1, bold: true), true));
        Assert.Equal(0x000003, snapshot.ResolveForeground(Attr(0x000003, mode: 1, bold: true), true));
    }

    [Fact]
    public void ResolveForeground_NotBold_IsUnaffectedByEitherBoldSetting()
    {
        var snapshot = new ColorPalette(new ThemeOptions { Foreground = "#010203", Bold = "#ffaa00" }).Take();

        Assert.Equal(0x010203, snapshot.ResolveForeground(Attr(DefaultFg), true));
        Assert.Equal(snapshot[1], snapshot.ResolveForeground(Attr(1), true));
        Assert.Equal(0x123456, snapshot.ResolveForeground(Attr(0x123456, mode: 1), true));
    }

    [Fact]
    public void ResolveForeground_WithNoBoldColor_KeepsTodaysBehaviour()
    {
        var snapshot = new ColorPalette(new ThemeOptions { Foreground = "#010203" }).Take();

        Assert.Equal(0x010203, snapshot.ResolveForeground(Attr(DefaultFg, bold: true), true));
        Assert.Equal(snapshot[9], snapshot.ResolveForeground(Attr(1, bold: true), true));
        Assert.Equal(snapshot[1], snapshot.ResolveForeground(Attr(1, bold: true), false));
    }

    /// <summary>
    /// Drives each foreground encoding through real SGR rather than hand-built attributes, so the
    /// mode check in ResolveForeground is tested against what the parser actually stores.
    /// </summary>
    [Theory]
    // default (SGR 39): colour 256, mode 0 -> the bold colour
    [InlineData("1;39", 256, 0, 0xFFAA00)]
    // palette-16 (SGR 31): colour 1, mode 0 -> brightened to index 9
    [InlineData("1;31", 1, 0, 0xFF0000)]
    // palette-256 (SGR 38;5;n): index n, int mode 0 like the sixteen, but never brightened --
    // even an index below 8, which names the same entry as SGR 31 but is drawn as named
    [InlineData("1;38;5;100", 100, 0, 0x878700)]
    [InlineData("1;38;5;1", 1, 0, 0xCD0000)]
    [InlineData("1;38;5;9", 9, 0, 0xFF0000)]
    // RGB (SGR 38;2): mode 1. 0x000100 is numerically 256, the default marker, and must
    // still resolve as the colour the program named -- this is what the mode check is for.
    [InlineData("1;38;2;0;1;0", 0x000100, 1, 0x000100)]
    [InlineData("1;38;2;0;0;3", 0x000003, 1, 0x000003)]
    public void ResolveForeground_EachSgrEncoding_IsReadByItsMode(string sgr, int color, int mode, int expected)
    {
        var terminal = CreateTerminal(new ThemeOptions { Foreground = "#010203", Bold = "#ffaa00" });
        terminal.Write($"\x1B[{sgr}mX");

        var attributes = terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes;
        Assert.True(attributes.IsBold());
        Assert.Equal(color, attributes.GetFgColor());
        Assert.Equal(mode, attributes.GetFgColorMode());
        Assert.Equal(expected, terminal.Colors.Take().ResolveForeground(attributes, true));
    }

    [Fact]
    public void ResolveForeground_Bold31Brightens_Bold38_5_1DoesNot()
    {
        var snapshot = new ColorPalette().Take();

        Assert.Equal(snapshot[9], ResolveBoldForeground("31"));
        Assert.Equal(snapshot[1], ResolveBoldForeground("38;5;1"));
        Assert.Equal(snapshot[1], ResolveBoldForeground("38:5:1"));
        Assert.Equal(snapshot[9], ResolveBoldForeground("38;5;9"));
        Assert.Equal(snapshot[9], ResolveBoldForeground("91"));
    }

    private int ResolveBoldForeground(string color)
    {
        var terminal = CreateTerminal();
        terminal.Write(Esc.Csi($"1;{color}m") + "X");

        var attributes = terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes;
        return terminal.Colors.Take().ResolveForeground(attributes, true);
    }

    /// <summary>
    /// Every background encoding, through real SGR. Bold is set throughout so a background that
    /// picked up the foreground's brightening would show.
    /// </summary>
    [Theory]
    [InlineData("49", null)]
    [InlineData("41", 0xCD0000)]
    [InlineData("101", 0xFF0000)]
    [InlineData("48;5;1", 0xCD0000)]
    [InlineData("48:5:1", 0xCD0000)]
    [InlineData("48;5;100", 0x878700)]
    [InlineData("48;2;1;2;3", 0x010203)]
    [InlineData("48:2::1:2:3", 0x010203)]
    // 0x000101 is numerically 257, the default background's value, and must still be RGB.
    [InlineData("48;2;0;1;1", 0x000101)]
    [InlineData("48;2;0;0;3", 0x000003)]
    public void ResolveBackground_EachSgrEncoding_ResolvesToTheColourNamed(string sgr, int? expected)
    {
        var terminal = CreateTerminal(new ThemeOptions { Background = "#102030" });
        terminal.Write(Esc.Csi($"1;{sgr}m") + "X");

        var attributes = terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes;
        Assert.Equal(expected, terminal.Colors.Take().ResolveBackground(attributes));
    }

    [Fact]
    public void ResolveBackground_ReadsThePaletteAsItStands()
    {
        var snapshot = new ColorPalette(new ThemeOptions { Red = "#ff8800" }).Take();
        var attributes = AttributeData.Default;

        attributes.SetBgColor(1);
        Assert.Equal(0xFF8800, snapshot.ResolveBackground(attributes));

        attributes.SetBgColor(1, AttributeData.Palette256Mode);
        Assert.Equal(0xFF8800, snapshot.ResolveBackground(attributes));

        Assert.Null(snapshot.ResolveBackground(AttributeData.Default));
    }

    [Fact]
    public void ThemeBold_IsLive_AssigningANewThemeChangesItAndRaisesColorChanged()
    {
        var terminal = CreateTerminal(new ThemeOptions { Foreground = "#010203" });
        var events = new List<ColorTarget>();
        terminal.Colors.ColorChanged += (_, e) => events.Add(e.Target);

        // Identical apart from Bold, so the bold colour alone must count as a change.
        terminal.Options.Theme = new ThemeOptions { Foreground = "#010203", Bold = "#ffaa00" };

        Assert.Equal(0xFFAA00, terminal.Colors.Bold);
        Assert.Equal(new[] { ColorTarget.All }, events);

        terminal.Options.Theme = new ThemeOptions { Foreground = "#010203" };
        Assert.Null(terminal.Colors.Bold);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void ThemeBold_SurvivesSingleColorChanges()
    {
        var palette = new ColorPalette(new ThemeOptions { Bold = "#ffaa00" });
        palette.SetColor(3, 0x111111);
        palette.SetForeground(0x222222);
        palette.ResetAllColors();
        Assert.Equal(0xFFAA00, palette.Bold);
    }

    // ---- Reply terminator mirrors the query's ------------------------------------------------

    [Fact]
    public void OscReplies_MirrorTheQueryTerminator()
    {
        var terminal = CreateTerminal();
        var replies = CaptureReplies(terminal);
        terminal.Write("\x1B]4;1;?\x07");           // BEL in, BEL out
        terminal.Write("\x1B]4;1;?\x1B\\");         // ST in, ST out
        terminal.Write("\x1B]10;?\x1B\\");
        Assert.Equal(3, replies.Count);
        Assert.EndsWith("\u0007", replies[0]);
        Assert.EndsWith("\u001b\\", replies[1]);
        Assert.EndsWith("\u001b\\", replies[2]);
    }
}
