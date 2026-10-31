using System.Runtime.InteropServices;

namespace XTerm.Buffer;

/// <summary>
/// Represents attribute data for a cell in the terminal buffer.
/// Stores foreground color, background color, and text attributes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AttributeData : IEquatable<AttributeData>
{
    /// <summary>
    /// Three ints, twelve bytes, in every cell of the buffer.
    /// </summary>
    /// <remarks>
    /// <para><c>Fg</c> and <c>Bg</c> each hold a colour in bits 0-24, its mode (palette or RGB) in
    /// bit 25, and in bit 26 a flag marking a palette index as the 256-colour palette's (SGR 38;5)
    /// rather than the sixteen's.
    /// <c>Extended</c> holds the boolean attributes in bits 0-8, the underline style in 9-11, and an
    /// underline colour id in 12-31.</para>
    /// <para>Underline colour is an id rather than a colour because a full RGB value plus its mode
    /// does not fit in the bits left, and growing this struct grows every cell — the thing measured
    /// as costing most on fills. See <see cref="Common.UnderlineColorTable"/> for why interning it
    /// adds nothing to the write path, unlike interning a whole style.</para>
    /// </remarks>
    public int Fg;
    public int Bg;
    public int Extended;

    // Attribute flags stored in upper bits of fg/bg
    private const int BOLD = 1 << 0;
    private const int DIM = 1 << 1;
    private const int ITALIC = 1 << 2;
    private const int UNDERLINE = 1 << 3;
    private const int BLINK = 1 << 4;
    private const int INVERSE = 1 << 5;
    private const int INVISIBLE = 1 << 6;
    private const int STRIKETHROUGH = 1 << 7;
    private const int OVERLINE = 1 << 8;

    // Underline style in bits 9-11, underline colour id in 12-29, protection in 30-31. The
    // colour is an id into an interning table, not a colour value, so eighteen bits is tens of
    // thousands of distinct underline colours per session -- far past any real screen -- and
    // giving two of its bits to protection costs nothing anyone can produce.
    private const int UNDERLINE_STYLE_SHIFT = 9;
    private const int UNDERLINE_STYLE_MASK = 0x7 << UNDERLINE_STYLE_SHIFT;
    private const int UNDERLINE_COLOR_SHIFT = 12;
    private const uint UNDERLINE_COLOR_MASK = 0x3FFFFu << UNDERLINE_COLOR_SHIFT;

    // DECSCA's protection and ISO 6429's guard (SPA/EPA) are INDEPENDENT: DECSED and DECSEL
    // honour the first, ED/EL/ECH honour the second, and neither implies the other.
    private const int PROTECTED = 1 << 30;
    private const int GUARDED = unchecked((int)0x80000000);

    public static AttributeData Default => new AttributeData
    {
        Fg = 256,  // Default foreground
        Bg = 257,  // Default background
        Extended = 0
    };

    public AttributeData()
    {
        Fg = 256;
        Bg = 257;
        Extended = 0;
    }

    public AttributeData(int fg, int bg, int extended)
    {
        Fg = fg;
        Bg = bg;
        Extended = extended;
    }

    /// <summary>
    /// Copy constructor for cloning.
    /// </summary>
    public AttributeData(AttributeData other)
    {
        Fg = other.Fg;
        Bg = other.Bg;
        Extended = other.Extended;
    }

    public readonly bool IsBold() => (Extended & BOLD) != 0;
    public readonly bool IsDim() => (Extended & DIM) != 0;
    public readonly bool IsItalic() => (Extended & ITALIC) != 0;
    public readonly bool IsUnderline() => GetUnderlineStyle() != Common.UnderlineStyle.None;
    public readonly bool IsBlink() => (Extended & BLINK) != 0;
    public readonly bool IsInverse() => (Extended & INVERSE) != 0;
    public readonly bool IsInvisible() => (Extended & INVISIBLE) != 0;
    public readonly bool IsStrikethrough() => (Extended & STRIKETHROUGH) != 0;
    public readonly bool IsOverline() => (Extended & OVERLINE) != 0;

    /// <summary>DECSCA protection: honoured by the selective erases, DECSED and DECSEL.</summary>
    public readonly bool IsProtected() => (Extended & PROTECTED) != 0;

    /// <summary>ISO guard (SPA/EPA): honoured by ED, EL and ECH.</summary>
    public readonly bool IsGuarded() => (Extended & GUARDED) != 0;

    public void SetBold(bool value) => SetFlag(BOLD, value);
    public void SetDim(bool value) => SetFlag(DIM, value);
    public void SetItalic(bool value) => SetFlag(ITALIC, value);
    /// <summary>
    /// Plain underline on or off, which is what SGR 4 and 24 mean.
    /// </summary>
    /// <remarks>
    /// Turning it on sets the SINGLE style rather than a flag, so the style is the one source of
    /// truth. Two places to say the same thing is how a cell ends up underlined by one and not the
    /// other.
    /// </remarks>
    public void SetUnderline(bool value)
        => SetUnderlineStyle(value ? Common.UnderlineStyle.Single : Common.UnderlineStyle.None);
    public void SetBlink(bool value) => SetFlag(BLINK, value);
    public void SetInverse(bool value) => SetFlag(INVERSE, value);
    public void SetInvisible(bool value) => SetFlag(INVISIBLE, value);
    public void SetStrikethrough(bool value) => SetFlag(STRIKETHROUGH, value);
    public void SetOverline(bool value) => SetFlag(OVERLINE, value);
    public void SetProtected(bool value) => SetFlag(PROTECTED, value);
    public void SetGuarded(bool value) => SetFlag(GUARDED, value);

    private void SetFlag(int flag, bool value)
    {
        if (value)
            Extended |= flag;
        else
            Extended &= ~flag;
    }

    public readonly int GetFgColor() => Fg & 0x1FFFFFF;
    public readonly int GetBgColor() => Bg & 0x1FFFFFF;

    /// <summary>
    /// The foreground's mode: 0 (<see cref="Common.ColorMode.Palette256"/>) for any palette index
    /// or the default colour 256, 1 (<see cref="Common.ColorMode.RGB"/>) for RGB.
    /// </summary>
    /// <remarks>
    /// It does not say WHICH palette: <c>31</c> and <c>38;5;1</c> are both 0 here. Use
    /// <see cref="GetFgColorKind"/> for that.
    /// </remarks>
    public readonly int GetFgColorMode() => (Fg >> 25) & 1;

    /// <summary>
    /// The background's mode, read as <see cref="GetFgColorMode"/> is; the default background is
    /// colour 257.
    /// </summary>
    public readonly int GetBgColorMode() => (Bg >> 25) & 1;

    /// <summary>
    /// Which kind of colour <see cref="GetFgColor"/> is: the 16-colour palette, the 256-colour
    /// palette, RGB, or the default foreground.
    /// </summary>
    public readonly Common.ColorMode GetFgColorKind() => KindOf(Fg);

    /// <summary>
    /// Which kind of colour <see cref="GetBgColor"/> is: the 16-colour palette, the 256-colour
    /// palette, RGB, or the default background.
    /// </summary>
    public readonly Common.ColorMode GetBgColorKind() => KindOf(Bg);

    /// <summary>
    /// The mode <see cref="SetFgColor"/> and <see cref="SetBgColor"/> take for an index from SGR
    /// 38;5 or 48;5: palette mode 0 plus the bit above it that says "256-colour palette".
    /// </summary>
    /// <remarks>
    /// A flag on the 256-colour palette, not on the sixteen, so SGR 30-37 store exactly what they
    /// always did, and so does a host that calls <c>SetFgColor(index)</c> itself. The int mode
    /// accessors mask the flag off, so it changes nothing a host reads through them.
    /// </remarks>
    internal const int Palette256Mode = 2;

    /// <summary>The colour value's bits in a packed <see cref="Fg"/> or <see cref="Bg"/>.</summary>
    internal const int ColorMask = 0x1FFFFFF;

    private const int PALETTE256 = Palette256Mode << 25;

    /// <summary>
    /// The kind of a packed <see cref="Fg"/> or <see cref="Bg"/> value, for a caller that has
    /// already read the field into a local and wants it read once.
    /// </summary>
    internal static Common.ColorMode KindOf(int packed)
    {
        if ((packed & (1 << 25)) != 0)
            return Common.ColorMode.RGB;

        var color = packed & 0x1FFFFFF;
        if (color >= Common.ColorPalette.Size)
            return Common.ColorMode.Default;

        // An index past 15 can only be the 256-colour palette, flagged or not.
        return (packed & PALETTE256) != 0 || color >= 16
            ? Common.ColorMode.Palette256
            : Common.ColorMode.Palette16;
    }

    public void SetFgColor(int color, int mode = 0)
    {
        Fg = (mode << 25) | (color & 0x1FFFFFF);
    }

    public void SetBgColor(int color, int mode = 0)
    {
        Bg = (mode << 25) | (color & 0x1FFFFFF);
    }

    /// <summary>How this cell's underline is drawn.</summary>
    public readonly Common.UnderlineStyle GetUnderlineStyle()
        => (Common.UnderlineStyle)((Extended & UNDERLINE_STYLE_MASK) >> UNDERLINE_STYLE_SHIFT);

    public void SetUnderlineStyle(Common.UnderlineStyle style)
        => Extended = (Extended & ~UNDERLINE_STYLE_MASK)
                      | ((((int)style) << UNDERLINE_STYLE_SHIFT) & UNDERLINE_STYLE_MASK);

    /// <summary>
    /// The interned id of this cell's underline colour, or zero for "same as the foreground".
    /// </summary>
    public readonly int GetUnderlineColorId()
        => (int)(((uint)Extended & UNDERLINE_COLOR_MASK) >> UNDERLINE_COLOR_SHIFT);

    /// <summary>
    /// Sets the underline colour, interning it so the cell carries only an id.
    /// </summary>
    /// <remarks>
    /// Full RGB is representable; it is the count of DISTINCT colours that is bounded, at about a
    /// million. See <see cref="Common.UnderlineColorTable"/>.
    /// </remarks>
    public void SetUnderlineColor(int color, int mode)
        => SetUnderlineColorId(Common.UnderlineColorTable.Intern(color, mode));

    /// <summary>Clears the underline colour, so the underline follows the foreground again.</summary>
    public void ResetUnderlineColor() => SetUnderlineColorId(Common.UnderlineColorTable.None);

    private void SetUnderlineColorId(int id)
        => Extended = (int)(((uint)Extended & ~UNDERLINE_COLOR_MASK)
                            | (((uint)id << UNDERLINE_COLOR_SHIFT) & UNDERLINE_COLOR_MASK));

    /// <summary>
    /// The underline's colour, or false when it follows the foreground.
    /// </summary>
    public readonly bool TryGetUnderlineColor(out int color, out int mode)
        => Common.UnderlineColorTable.TryGet(GetUnderlineColorId(), out color, out mode);

    public readonly bool Equals(AttributeData other)
    {
        return Fg == other.Fg && Bg == other.Bg && Extended == other.Extended;
    }

    public readonly override bool Equals(object? obj)
    {
        return obj is AttributeData other && Equals(other);
    }

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(Fg, Bg, Extended);
    }

    public static bool operator ==(AttributeData left, AttributeData right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(AttributeData left, AttributeData right)
    {
        return !left.Equals(right);
    }

}
