using System.Diagnostics;
using System.Text;
using XTerm.Common;

namespace XTerm.Buffer;

/// <summary>
/// Represents a single cell in the terminal buffer.
/// Each cell contains a character, width, and attributes.
/// </summary>
[DebuggerDisplay("'{Content}'  [{Width}, {Attributes}, {CodePoint}]")]
public struct BufferCell : IEquatable<BufferCell>
{
    public string Content = String.Empty;
    public int Width = 0;
    public AttributeData Attributes = AttributeData.Default;
    public int CodePoint = 0;

    public static BufferCell Empty => new BufferCell();

    public static BufferCell Space => new BufferCell
    {
        Content = " ",
        Width = 1,
        Attributes = AttributeData.Default,
        CodePoint = 0x20
    };

    public BufferCell()
    {
        Content = String.Empty;
        Attributes = AttributeData.Default;
    }
    public BufferCell(string content, int width, AttributeData attributes)
    {
        Content = content;
        Width = width;
        Attributes = attributes;
        CodePoint = content.Length > 0 ? char.ConvertToUtf32(content, 0) : 0;
    }

    public BufferCell(int codePoint, int width, AttributeData attributes)
    {
        CodePoint = codePoint;
        Width = width;
        Attributes = attributes;
        Content = char.ConvertFromUtf32(codePoint);
    }

    public bool IsEmpty() => CodePoint == Empty.CodePoint;

    public bool IsSpace() => CodePoint == Space.CodePoint;

    public bool Equals(BufferCell other)
    {
        return Content == other.Content &&
               Width == other.Width &&
               Attributes.Equals(other.Attributes) &&
               CodePoint == other.CodePoint;
    }

    public override bool Equals(object? obj)
    {
        return obj is BufferCell other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Content, Width, Attributes, CodePoint);
    }

    public static bool operator ==(BufferCell left, BufferCell right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BufferCell left, BufferCell right)
    {
        return !left.Equals(right);
    }
}
