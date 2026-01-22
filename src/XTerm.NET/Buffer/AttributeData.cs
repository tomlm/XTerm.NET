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
    /// Bit layout:
    /// 0-8: Foreground color (9 bits)
    /// 9-17: Background color (9 bits)
    /// 18-26: Extended attributes (9 bits)
    /// 27-31: Flags (5 bits)
    /// </summary>
    public int Fg;
    public int Bg;
    public int Extended;

    private const int FG_MASK = 0x1FF;
    private const int BG_MASK = 0x1FF << 9;
    private const int EXT_MASK = 0x1FF << 18;

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

    public bool IsBold() => (Extended & BOLD) != 0;
    public bool IsDim() => (Extended & DIM) != 0;
    public bool IsItalic() => (Extended & ITALIC) != 0;
    public bool IsUnderline() => (Extended & UNDERLINE) != 0;
    public bool IsBlink() => (Extended & BLINK) != 0;
    public bool IsInverse() => (Extended & INVERSE) != 0;
    public bool IsInvisible() => (Extended & INVISIBLE) != 0;
    public bool IsStrikethrough() => (Extended & STRIKETHROUGH) != 0;
    public bool IsOverline() => (Extended & OVERLINE) != 0;

    public void SetBold(bool value) => SetFlag(BOLD, value);
    public void SetDim(bool value) => SetFlag(DIM, value);
    public void SetItalic(bool value) => SetFlag(ITALIC, value);
    public void SetUnderline(bool value) => SetFlag(UNDERLINE, value);
    public void SetBlink(bool value) => SetFlag(BLINK, value);
    public void SetInverse(bool value) => SetFlag(INVERSE, value);
    public void SetInvisible(bool value) => SetFlag(INVISIBLE, value);
    public void SetStrikethrough(bool value) => SetFlag(STRIKETHROUGH, value);
    public void SetOverline(bool value) => SetFlag(OVERLINE, value);

    private void SetFlag(int flag, bool value)
    {
        if (value)
            Extended |= flag;
        else
            Extended &= ~flag;
    }

    public int GetFgColor() => Fg & 0x1FFFFFF;
    public int GetBgColor() => Bg & 0x1FFFFFF;
    
    public int GetFgColorMode() => Fg >> 25;
    public int GetBgColorMode() => Bg >> 25;

    public void SetFgColor(int color, int mode = 0)
    {
        Fg = (mode << 25) | (color & 0x1FFFFFF);
    }

    public void SetBgColor(int color, int mode = 0)
    {
        Bg = (mode << 25) | (color & 0x1FFFFFF);
    }

    public bool Equals(AttributeData other)
    {
        return Fg == other.Fg && Bg == other.Bg && Extended == other.Extended;
    }

    public override bool Equals(object? obj)
    {
        return obj is AttributeData other && Equals(other);
    }

    public override int GetHashCode()
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
