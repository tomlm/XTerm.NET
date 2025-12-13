using System.Text;
using XTerm.NET.Common;

namespace XTerm.NET.Buffer;

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
        Content = Constants.NULL_CELL_CHAR.ToString(),
        Width = Constants.NULL_CELL_WIDTH,
        Attributes = AttributeData.Default,
        CodePoint = Constants.NULL_CELL_CODE
    };

    public static BufferCell Whitespace => new BufferCell
    {
        Content = Constants.WHITESPACE_CELL_CHAR.ToString(),
        Width = Constants.WHITESPACE_CELL_WIDTH,
        Attributes = AttributeData.Default,
        CodePoint = Constants.WHITESPACE_CELL_CODE
    };

    public BufferCell()
    {
        Content = Constants.NULL_CELL_CHAR.ToString();
        Width = Constants.NULL_CELL_WIDTH;
        Attributes = AttributeData.Default;
        CodePoint = Constants.NULL_CELL_CODE;
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

    public bool IsNull() => CodePoint == Constants.NULL_CELL_CODE && Width == Constants.NULL_CELL_WIDTH;

    public bool IsWhitespace() => CodePoint == Constants.WHITESPACE_CELL_CODE;

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

/// <summary>
/// Interface for cell data in the buffer.
/// </summary>
public interface ICellData
{
    string Content { get; set; }
    int Width { get; set; }
    AttributeData Attributes { get; set; }
    int GetCode();
    string GetChars();
    int GetWidth();
    bool IsNull();
}
