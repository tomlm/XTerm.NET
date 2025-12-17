using System.Text;
using XTerm.Common;

namespace XTerm.Buffer;

/// <summary>
/// Represents a single cell in the terminal buffer.
/// Each cell contains a character, width, and attributes.
/// </summary>
public struct BufferCell : IEquatable<BufferCell>
{
    public string Content;
    public int Width;
    public AttributeData Attributes;
    public int CodePoint;

    public static BufferCell Null => new BufferCell
    {
        Content = Constants.NullCellChar.ToString(),
        Width = Constants.NullCellWidth,
        Attributes = AttributeData.Default,
        CodePoint = Constants.NullCellCode
    };

    public static BufferCell Whitespace => new BufferCell
    {
        Content = Constants.WhitespaceCellChar.ToString(),
        Width = Constants.WhitespaceCellWidth,
        Attributes = AttributeData.Default,
        CodePoint = Constants.WhitespaceCellCode
    };

    public BufferCell()
    {
        Content = Constants.NullCellChar.ToString();
        Width = Constants.NullCellWidth;
        Attributes = AttributeData.Default;
        CodePoint = Constants.NullCellCode;
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

    public bool IsNull() => CodePoint == Constants.NullCellCode && Width == Constants.NullCellWidth;

    public bool IsWhitespace() => CodePoint == Constants.WhitespaceCellCode;

    public int GetWidth() => Width;

    public string GetChars() => Content;

    public int GetCode() => CodePoint;

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

    public BufferCell Clone()
    {
        return new BufferCell
        {
            Content = Content,
            Width = Width,
            Attributes = Attributes.Clone(),
            CodePoint = CodePoint
        };
    }
}
