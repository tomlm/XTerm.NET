using XTerm.Buffer;
using XTerm.Common;

namespace XTerm.Tests.Buffer;

public class BufferCellTests
{
    [Fact]
    public void Constructor_Default_SetsNullValues()
    {
        // Arrange & Act
        var cell = new BufferCell();

        // Assert
        Assert.Equal(Constants.NullCellChar.ToString(), cell.Content);
        Assert.Equal(Constants.NullCellWidth, cell.Width);
        Assert.Equal(Constants.NullCellCode, cell.CodePoint);
        Assert.Equal(AttributeData.Default, cell.Attributes);
    }

    [Fact]
    public void Constructor_WithContent_SetsValues()
    {
        // Arrange
        var content = "A";
        var width = 1;
        var attr = new AttributeData(10, 20, 0);

        // Act
        var cell = new BufferCell(content, width, attr);

        // Assert
        Assert.Equal(content, cell.Content);
        Assert.Equal(width, cell.Width);
        Assert.Equal('A', cell.CodePoint);
        Assert.Equal(attr, cell.Attributes);
    }

    [Fact]
    public void Constructor_WithCodePoint_SetsValues()
    {
        // Arrange
        var codePoint = 65; // 'A'
        var width = 1;
        var attr = new AttributeData(10, 20, 0);

        // Act
        var cell = new BufferCell(codePoint, width, attr);

        // Assert
        Assert.Equal("A", cell.Content);
        Assert.Equal(width, cell.Width);
        Assert.Equal(codePoint, cell.CodePoint);
        Assert.Equal(attr, cell.Attributes);
    }

    [Fact]
    public void Null_Property_ReturnsNullCell()
    {
        // Act
        var cell = BufferCell.Null;

        // Assert
        Assert.Equal(Constants.NullCellChar.ToString(), cell.Content);
        Assert.Equal(Constants.NullCellWidth, cell.Width);
        Assert.Equal(Constants.NullCellCode, cell.CodePoint);
    }

    [Fact]
    public void Whitespace_Property_ReturnsWhitespaceCell()
    {
        // Act
        var cell = BufferCell.Whitespace;

        // Assert
        Assert.Equal(Constants.WhitespaceCellChar.ToString(), cell.Content);
        Assert.Equal(Constants.WhitespaceCellWidth, cell.Width);
        Assert.Equal(Constants.WhitespaceCellCode, cell.CodePoint);
    }

    [Fact]
    public void IsNull_NullCell_ReturnsTrue()
    {
        // Arrange
        var cell = BufferCell.Null;

        // Act & Assert
        Assert.True(cell.IsNull());
    }

    [Fact]
    public void IsNull_NonNullCell_ReturnsFalse()
    {
        // Arrange
        var cell = new BufferCell("A", 1, AttributeData.Default);

        // Act & Assert
        Assert.False(cell.IsNull());
    }

    [Fact]
    public void IsWhitespace_WhitespaceCell_ReturnsTrue()
    {
        // Arrange
        var cell = BufferCell.Whitespace;

        // Act & Assert
        Assert.True(cell.IsWhitespace());
    }

    [Fact]
    public void IsWhitespace_NonWhitespaceCell_ReturnsFalse()
    {
        // Arrange
        var cell = new BufferCell("A", 1, AttributeData.Default);

        // Act & Assert
        Assert.False(cell.IsWhitespace());
    }

    [Fact]
    public void GetWidth_ReturnsWidth()
    {
        // Arrange
        var cell = new BufferCell("A", 2, AttributeData.Default);

        // Act
        var width = cell.GetWidth();

        // Assert
        Assert.Equal(2, width);
    }

    [Fact]
    public void GetChars_ReturnsContent()
    {
        // Arrange
        var content = "ABC";
        var cell = new BufferCell(content, 1, AttributeData.Default);

        // Act
        var chars = cell.GetChars();

        // Assert
        Assert.Equal(content, chars);
    }

    [Fact]
    public void GetCode_ReturnsCodePoint()
    {
        // Arrange
        var codePoint = 65;
        var cell = new BufferCell(codePoint, 1, AttributeData.Default);

        // Act
        var code = cell.GetCode();

        // Assert
        Assert.Equal(codePoint, code);
    }

    [Fact]
    public void Equals_SameCells_ReturnsTrue()
    {
        // Arrange
        var attr = new AttributeData(10, 20, 0);
        var cell1 = new BufferCell("A", 1, attr);
        var cell2 = new BufferCell("A", 1, attr);

        // Act & Assert
        Assert.True(cell1.Equals(cell2));
        Assert.True(cell1 == cell2);
    }

    [Fact]
    public void Equals_DifferentContent_ReturnsFalse()
    {
        // Arrange
        var attr = new AttributeData(10, 20, 0);
        var cell1 = new BufferCell("A", 1, attr);
        var cell2 = new BufferCell("B", 1, attr);

        // Act & Assert
        Assert.False(cell1.Equals(cell2));
        Assert.True(cell1 != cell2);
    }

    [Fact]
    public void Equals_DifferentWidth_ReturnsFalse()
    {
        // Arrange
        var attr = new AttributeData(10, 20, 0);
        var cell1 = new BufferCell("A", 1, attr);
        var cell2 = new BufferCell("A", 2, attr);

        // Act & Assert
        Assert.False(cell1.Equals(cell2));
    }

    [Fact]
    public void Equals_DifferentAttributes_ReturnsFalse()
    {
        // Arrange
        var attr1 = new AttributeData(10, 20, 0);
        var attr2 = new AttributeData(30, 40, 0);
        var cell1 = new BufferCell("A", 1, attr1);
        var cell2 = new BufferCell("A", 1, attr2);

        // Act & Assert
        Assert.False(cell1.Equals(cell2));
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var attr = new AttributeData(10, 20, 0);
        var cell = new BufferCell("A", 1, attr);

        // Act
        var clone = cell.Clone();

        // Assert
        Assert.Equal(cell.Content, clone.Content);
        Assert.Equal(cell.Width, clone.Width);
        Assert.Equal(cell.CodePoint, clone.CodePoint);
        Assert.Equal(cell.Attributes, clone.Attributes);
        
        // Verify it's a true copy (modifying attributes doesn't affect original)
        clone.Attributes.SetBold(true);
        Assert.NotEqual(cell.Attributes.IsBold(), clone.Attributes.IsBold());
    }

    [Fact]
    public void GetHashCode_SameCells_ReturnsSameHash()
    {
        // Arrange
        var attr = new AttributeData(10, 20, 0);
        var cell1 = new BufferCell("A", 1, attr);
        var cell2 = new BufferCell("A", 1, attr);

        // Act & Assert
        Assert.Equal(cell1.GetHashCode(), cell2.GetHashCode());
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("?", 2)] // Wide character
    [InlineData("??", 2)] // Emoji
    [InlineData(" ", 1)] // Space
    public void Constructor_VariousCharacters_HandlesCorrectly(string content, int expectedWidth)
    {
        // Arrange & Act
        var cell = new BufferCell(content, expectedWidth, AttributeData.Default);

        // Assert
        Assert.Equal(content, cell.Content);
        Assert.Equal(expectedWidth, cell.Width);
    }
}
