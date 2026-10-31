using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// What each SGR colour form stores: the value, the int mode hosts have always read, and the
/// precise <see cref="ColorMode"/> kind.
/// </summary>
/// <remarks>
/// The 16- and 256-colour palettes name the same entries below 16 but render differently -- bold
/// brightens only the first -- so the kind is the only thing that tells <c>31</c> from
/// <c>38;5;1</c>; the int mode reports 0 for both, as it always has. Every form is driven through the parser rather than built by hand, so the test
/// is of what the parser actually stores.
/// </remarks>
public class SgrColorModeTests
{
    private static readonly string EscChar = ((char)0x1B).ToString();

    private static AttributeData AttributesAfter(string sgr)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });
        terminal.Write(Esc.Csi($"{sgr}m") + "X");
        return terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes;
    }

    [Theory]
    [InlineData("30", 0, 0, ColorMode.Palette16)]
    [InlineData("31", 1, 0, ColorMode.Palette16)]
    [InlineData("37", 7, 0, ColorMode.Palette16)]
    [InlineData("90", 8, 0, ColorMode.Palette16)]
    [InlineData("97", 15, 0, ColorMode.Palette16)]
    [InlineData("38;5;1", 1, 0, ColorMode.Palette256)]
    [InlineData("38;5;9", 9, 0, ColorMode.Palette256)]
    [InlineData("38;5;15", 15, 0, ColorMode.Palette256)]
    [InlineData("38;5;16", 16, 0, ColorMode.Palette256)]
    [InlineData("38;5;255", 255, 0, ColorMode.Palette256)]
    [InlineData("38:5:1", 1, 0, ColorMode.Palette256)]
    [InlineData("38:5:200", 200, 0, ColorMode.Palette256)]
    [InlineData("38;2;1;2;3", 0x010203, 1, ColorMode.RGB)]
    [InlineData("38:2::1:2:3", 0x010203, 1, ColorMode.RGB)]
    // 0x000100 is numerically 256, the default foreground's value, and is still RGB.
    [InlineData("38;2;0;1;0", 0x000100, 1, ColorMode.RGB)]
    [InlineData("31;39", 256, 0, ColorMode.Default)]
    [InlineData("38;5;1;39", 256, 0, ColorMode.Default)]
    [InlineData("0", 256, 0, ColorMode.Default)]
    public void Foreground_EachForm_StoresItsValueAndMode(string sgr, int color, int mode, ColorMode kind)
    {
        var attributes = AttributesAfter(sgr);

        Assert.Equal(color, attributes.GetFgColor());
        Assert.Equal(mode, attributes.GetFgColorMode());
        Assert.Equal(kind, attributes.GetFgColorKind());
    }

    [Theory]
    [InlineData("40", 0, 0, ColorMode.Palette16)]
    [InlineData("41", 1, 0, ColorMode.Palette16)]
    [InlineData("47", 7, 0, ColorMode.Palette16)]
    [InlineData("100", 8, 0, ColorMode.Palette16)]
    [InlineData("107", 15, 0, ColorMode.Palette16)]
    [InlineData("48;5;1", 1, 0, ColorMode.Palette256)]
    [InlineData("48;5;9", 9, 0, ColorMode.Palette256)]
    [InlineData("48;5;16", 16, 0, ColorMode.Palette256)]
    [InlineData("48;5;255", 255, 0, ColorMode.Palette256)]
    [InlineData("48:5:1", 1, 0, ColorMode.Palette256)]
    [InlineData("48;2;1;2;3", 0x010203, 1, ColorMode.RGB)]
    [InlineData("48:2::1:2:3", 0x010203, 1, ColorMode.RGB)]
    // 0x000101 is numerically 257, the default background's value, and is still RGB.
    [InlineData("48;2;0;1;1", 0x000101, 1, ColorMode.RGB)]
    [InlineData("41;49", 257, 0, ColorMode.Default)]
    [InlineData("48;5;1;49", 257, 0, ColorMode.Default)]
    [InlineData("0", 257, 0, ColorMode.Default)]
    public void Background_EachForm_StoresItsValueAndMode(string sgr, int color, int mode, ColorMode kind)
    {
        var attributes = AttributesAfter(sgr);

        Assert.Equal(color, attributes.GetBgColor());
        Assert.Equal(mode, attributes.GetBgColorMode());
        Assert.Equal(kind, attributes.GetBgColorKind());
    }

    [Fact]
    public void ColorMode_ExistingMembers_KeepTheirValues()
    {
        // Palette256 and RGB were public before the kinds existed, and are what the int accessors
        // return; a host compiled against them must see the same numbers.
        Assert.Equal(0, (int)ColorMode.Palette256);
        Assert.Equal(1, (int)ColorMode.RGB);
    }

    /// <summary>
    /// Pins the int mode accessors to what they returned before the 16- and 256-colour palettes
    /// were told apart: 0 for every palette form and the defaults, 1 for RGB, on every side
    /// including the underline. Hosts branch on these numbers.
    /// </summary>
    [Theory]
    [InlineData("31", 0, null)]
    [InlineData("91", 0, null)]
    [InlineData("38;5;1", 0, null)]
    [InlineData("38;5;200", 0, null)]
    [InlineData("38:5:1", 0, null)]
    [InlineData("38;2;1;2;3", 1, null)]
    [InlineData("38:2::1:2:3", 1, null)]
    [InlineData("39", 0, null)]
    [InlineData("41", null, 0)]
    [InlineData("101", null, 0)]
    [InlineData("48;5;1", null, 0)]
    [InlineData("48;5;200", null, 0)]
    [InlineData("48:5:1", null, 0)]
    [InlineData("48;2;1;2;3", null, 1)]
    [InlineData("48:2::1:2:3", null, 1)]
    [InlineData("49", null, 0)]
    public void IntModes_AreWhatTheyWereBeforeTheKindsExisted(string sgr, int? fgMode, int? bgMode)
    {
        var attributes = AttributesAfter(sgr);

        if (fgMode is { } fg)
            Assert.Equal(fg, attributes.GetFgColorMode());
        if (bgMode is { } bg)
            Assert.Equal(bg, attributes.GetBgColorMode());
    }

    [Theory]
    [InlineData("58;5;196", 196, 0)]
    [InlineData("58:5:1", 1, 0)]
    [InlineData("58;2;1;2;3", 0x010203, 1)]
    [InlineData("58:2::1:2:3", 0x010203, 1)]
    public void UnderlineIntModes_AreWhatTheyWereBeforeTheKindsExisted(string sgr, int color, int mode)
    {
        Assert.True(AttributesAfter(sgr).TryGetUnderlineColor(out var actualColor, out var actualMode));
        Assert.Equal(color, actualColor);
        Assert.Equal(mode, actualMode);
    }

    [Theory]
    [InlineData(1, 0, ColorMode.Palette16)]
    [InlineData(15, 0, ColorMode.Palette16)]
    [InlineData(100, 0, ColorMode.Palette256)]
    [InlineData(256, 0, ColorMode.Default)]
    [InlineData(0x123456, 1, ColorMode.RGB)]
    public void HostBuiltColours_ThroughTheIntSetter_ReadAsBefore(int color, int mode, ColorMode kind)
    {
        // A host that builds attributes with SetFgColor(index) gets what it always got, and the
        // flag never leaks into the colour value.
        var attributes = AttributeData.Default;
        attributes.SetFgColor(color, mode);
        attributes.SetBgColor(color, mode);

        Assert.Equal(color, attributes.GetFgColor());
        Assert.Equal(mode, attributes.GetFgColorMode());
        Assert.Equal(kind, attributes.GetFgColorKind());
        Assert.Equal(color, attributes.GetBgColor());
        Assert.Equal(mode, attributes.GetBgColorMode());
        Assert.Equal(kind, attributes.GetBgColorKind());
    }

    /// <summary>
    /// An index past the 256-colour palette is ignored, as xterm ignores it, and still consumed,
    /// so the parameters after it keep their meaning. Stored, it would read as the default colour
    /// -- in the theme's bold colour when bold.
    /// </summary>
    [Theory]
    [InlineData("31;38;5;256", 1, ColorMode.Palette16)]
    [InlineData("38;5;1;38;5;300", 1, ColorMode.Palette256)]
    [InlineData("38;5;300", 256, ColorMode.Default)]
    [InlineData("31;38:5:300", 1, ColorMode.Palette16)]
    [InlineData("38;5;300;32", 2, ColorMode.Palette16)]
    public void Foreground_IndexPastThePalette_IsIgnored(string sgr, int color, ColorMode kind)
    {
        var attributes = AttributesAfter(sgr);

        Assert.Equal(color, attributes.GetFgColor());
        Assert.Equal(kind, attributes.GetFgColorKind());
    }

    [Theory]
    [InlineData("42;48;5;256", 2, ColorMode.Palette16)]
    [InlineData("48;5;300", 257, ColorMode.Default)]
    [InlineData("42;48:5:300", 2, ColorMode.Palette16)]
    [InlineData("48;5;300;43", 3, ColorMode.Palette16)]
    public void Background_IndexPastThePalette_IsIgnored(string sgr, int color, ColorMode kind)
    {
        var attributes = AttributesAfter(sgr);

        Assert.Equal(color, attributes.GetBgColor());
        Assert.Equal(kind, attributes.GetBgColorKind());
    }

    [Fact]
    public void IndexPastThePalette_ConsumesItsParameter_SoTheNextOneStillApplies()
    {
        // 256 is read as the index, not as an SGR of its own, and the 4 after it underlines.
        var attributes = AttributesAfter("1;38;5;256;4");

        Assert.True(attributes.IsBold());
        Assert.True(attributes.IsUnderline());
        Assert.Equal(ColorMode.Default, attributes.GetFgColorKind());
    }

    [Theory]
    [InlineData("58;5;300")]
    [InlineData("58:5:300")]
    public void Underline_IndexPastThePalette_IsIgnored(string sgr)
    {
        Assert.False(AttributesAfter(sgr).TryGetUnderlineColor(out _, out _));
    }

    /// <summary>
    /// The resolvers take <c>in AttributeData</c>; a getter that is not <c>readonly</c> makes the
    /// compiler copy the whole struct before every call through that reference.
    /// </summary>
    [Fact]
    public void AttributeData_PureGetters_AreReadonly()
    {
        var getters = typeof(AttributeData)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.Name.StartsWith("Is") || m.Name.StartsWith("Get") || m.Name.StartsWith("TryGet")
                        || m.Name == nameof(AttributeData.Equals))
            .ToList();

        Assert.Contains(getters, m => m.Name == nameof(AttributeData.GetFgColorKind));
        Assert.Contains(getters, m => m.Name == nameof(AttributeData.IsBold));

        var notReadonly = getters
            .Where(m => !m.CustomAttributes.Any(a => a.AttributeType.Name == "IsReadOnlyAttribute"))
            .Select(m => m.Name);
        Assert.Empty(notReadonly);
    }

    [Fact]
    public void Sixteen_And256Palette_SameIndex_AreDifferentAttributes()
    {
        // The equality a renderer coalesces runs on must split them: bold draws them differently.
        Assert.NotEqual(AttributesAfter("31"), AttributesAfter("38;5;1"));
        Assert.NotEqual(AttributesAfter("41"), AttributesAfter("48;5;1"));
        Assert.Equal(AttributesAfter("38;5;1"), AttributesAfter("38:5:1"));
    }

    [Theory]
    [InlineData("1;38;5;1;48;5;2")]
    [InlineData("1;31;42")]
    [InlineData("1;38;5;9;48;5;15")]
    [InlineData("1;91;48;2;0;1;1")]
    public void Decrqss_ReplaysToTheSameAttributes(string sgr)
    {
        var source = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });
        source.Write(Esc.Csi($"{sgr}m") + "X");
        var reply = RequestSgr(source);

        var replayed = new Terminal(new TerminalOptions { Cols = 10, Rows = 2 });
        replayed.Write(Esc.Csi($"{reply}m") + "X");

        Assert.Equal(
            source.Buffer.Lines[source.Buffer.YBase]![0].Attributes,
            replayed.Buffer.Lines[replayed.Buffer.YBase]![0].Attributes);
    }

    private static string RequestSgr(Terminal terminal)
    {
        var reply = new System.Text.StringBuilder();
        terminal.DataReceived += (_, e) => reply.Append(e.Data);
        terminal.Write($"{EscChar}P$qm{Esc.St}");

        var prefix = $"{EscChar}P1$r";
        var suffix = $"m{Esc.St}";
        var text = reply.ToString();
        Assert.StartsWith(prefix, text);
        Assert.EndsWith(suffix, text);
        return text[prefix.Length..^suffix.Length];
    }
}
