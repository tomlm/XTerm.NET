using XTerm.Buffer;

namespace XTerm.Tests.Buffer;

public class BufferLineTests
{
    [Fact]
    public void Constructor_CreatesLineWithSpecifiedColumns()
    {
        // Arrange
        var cols = 80;

        // Act
        var line = new BufferLine(cols);

        // Assert
        Assert.Equal(cols, line.Length);
        Assert.False(line.IsWrapped);
    }

    [Fact]
    public void Constructor_WithFillCell_FillsAllCells()
    {
        // Arrange
        var cols = 10;
        var fillCell = new BufferCell("X", 1, AttributeData.Default);

        // Act
        var line = new BufferLine(cols, fillCell);

        // Assert
        for (int i = 0; i < cols; i++)
        {
            Assert.Equal("X", line[i].Content);
        }
    }

    [Fact]
    public void Indexer_Get_ReturnsCell()
    {
        // Arrange
        var line = new BufferLine(10);
        var cell = new BufferCell("A", 1, AttributeData.Default);
        line[5] = cell;

        // Act
        var retrieved = line[5];

        // Assert
        Assert.Equal("A", retrieved.Content);
    }

    [Fact]
    public void Indexer_Set_SetsCell()
    {
        // Arrange
        var line = new BufferLine(10);
        var cell = new BufferCell("B", 1, AttributeData.Default);

        // Act
        line[3] = cell;

        // Assert
        Assert.Equal("B", line[3].Content);
    }

    [Fact]
    public void Indexer_OutOfBounds_ReturnsNullCell()
    {
        // Arrange
        var line = new BufferLine(10);

        // Act
        var cell = line[-1];
        var cell2 = line[100];

        // Assert
        Assert.True(cell.IsEmpty());
        Assert.True(cell2.IsEmpty());
    }

    [Fact]
    public void Indexer_Set_OutOfBounds_DoesNotThrow()
    {
        // Arrange
        var line = new BufferLine(10);
        var cell = new BufferCell("X", 1, AttributeData.Default);

        // Act & Assert - Should not throw
        line[-1] = cell;
        line[100] = cell;
    }

    [Fact]
    public void SetCell_SetsCell()
    {
        // Arrange
        var line = new BufferLine(10);
        var cell = new BufferCell("D", 1, AttributeData.Default);

        // Act
        line.SetCell(4, ref cell);

        // Assert
        Assert.Equal("D", line[4].Content);
    }

    [Fact]
    public void GetCodePoint_ReturnsCodePoint()
    {
        // Arrange
        var line = new BufferLine(10);
        var cell = new BufferCell(65, 1, AttributeData.Default); // 'A'
        line[6] = cell;

        // Act
        var code = line.GetCodePoint(6);

        // Assert
        Assert.Equal(65, code);
    }

    [Fact]
    public void GetCodePoint_OutOfBounds_ReturnsZero()
    {
        // Arrange
        var line = new BufferLine(10);

        // Act
        var code = line.GetCodePoint(-1);
        var code2 = line.GetCodePoint(100);

        // Assert
        Assert.Equal(0, code);
        Assert.Equal(0, code2);
    }

    [Fact]
    public void Resize_Expand_AddsNewCells()
    {
        // Arrange
        var line = new BufferLine(10);
        var fillCell = new BufferCell("X", 1, AttributeData.Default);
        line[5] = new BufferCell("A", 1, AttributeData.Default);

        // Act
        line.Resize(20, fillCell);

        // Assert
        Assert.Equal(20, line.Length);
        Assert.Equal("A", line[5].Content); // Original data preserved
        Assert.Equal("X", line[15].Content); // New cells filled
    }

    [Fact]
    public void Resize_Shrink_TruncatesCells()
    {
        // Arrange
        var line = new BufferLine(20);
        var fillCell = BufferCell.Empty;
        line[15] = new BufferCell("A", 1, AttributeData.Default);

        // Act
        line.Resize(10, fillCell);

        // Assert
        Assert.Equal(10, line.Length);
    }

    [Fact]
    public void Resize_SameSize_DoesNothing()
    {
        // Arrange
        var line = new BufferLine(10);
        var fillCell = BufferCell.Empty;
        line[5] = new BufferCell("A", 1, AttributeData.Default);

        // Act
        line.Resize(10, fillCell);

        // Assert
        Assert.Equal(10, line.Length);
        Assert.Equal("A", line[5].Content);
    }

    [Fact]
    public void Fill_FillsRange()
    {
        // Arrange
        var line = new BufferLine(10);
        var fillCell = new BufferCell("F", 1, AttributeData.Default);

        // Act
        line.Fill(fillCell, 2, 5);

        // Assert
        Assert.True(line[1].IsSpace()); // Before range
        Assert.Equal("F", line[2].Content);
        Assert.Equal("F", line[3].Content);
        Assert.Equal("F", line[4].Content);
        Assert.True(line[5].IsSpace()); // After range
    }

    [Fact]
    public void Fill_NoParameters_FillsEntireLine()
    {
        // Arrange
        var line = new BufferLine(10);
        var fillCell = new BufferCell("G", 1, AttributeData.Default);

        // Act
        line.Fill(fillCell);

        // Assert
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal("G", line[i].Content);
        }
    }

    [Fact]
    public void CopyCellsFrom_Forward_CopiesCells()
    {
        // Arrange
        var srcLine = new BufferLine(10);
        var destLine = new BufferLine(10);
        srcLine[2] = new BufferCell("A", 1, AttributeData.Default);
        srcLine[3] = new BufferCell("B", 1, AttributeData.Default);
        srcLine[4] = new BufferCell("C", 1, AttributeData.Default);

        // Act
        destLine.CopyCellsFrom(srcLine, 2, 5, 3, false);

        // Assert
        Assert.Equal("A", destLine[5].Content);
        Assert.Equal("B", destLine[6].Content);
        Assert.Equal("C", destLine[7].Content);
    }

    [Fact]
    public void CopyCellsFrom_Reverse_CopiesCells()
    {
        // Arrange
        var srcLine = new BufferLine(10);
        var destLine = new BufferLine(10);
        srcLine[2] = new BufferCell("A", 1, AttributeData.Default);
        srcLine[3] = new BufferCell("B", 1, AttributeData.Default);
        srcLine[4] = new BufferCell("C", 1, AttributeData.Default);

        // Act
        destLine.CopyCellsFrom(srcLine, 2, 5, 3, true);

        // Assert
        Assert.Equal("A", destLine[5].Content);
        Assert.Equal("B", destLine[6].Content);
        Assert.Equal("C", destLine[7].Content);
    }

    [Fact]
    public void TranslateToString_ConvertsLineToString()
    {
        // Arrange
        var line = new BufferLine(5);
        line[0] = new BufferCell("H", 1, AttributeData.Default);
        line[1] = new BufferCell("e", 1, AttributeData.Default);
        line[2] = new BufferCell("l", 1, AttributeData.Default);
        line[3] = new BufferCell("l", 1, AttributeData.Default);
        line[4] = new BufferCell("o", 1, AttributeData.Default);

        // Act
        var result = line.TranslateToString();

        // Assert
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void TranslateToString_TrimRight_TrimsWhitespace()
    {
        // Arrange
        var line = new BufferLine(10);
        line[0] = new BufferCell("H", 1, AttributeData.Default);
        line[1] = new BufferCell("i", 1, AttributeData.Default);
        // Rest are null/spaces

        // Act
        var result = line.TranslateToString(trimRight: true);

        // Assert
        Assert.Equal("Hi", result.TrimEnd());
    }

    [Fact]
    public void TranslateToString_WithRange_ConvertsRange()
    {
        // Arrange
        var line = new BufferLine(10);
        line[2] = new BufferCell("A", 1, AttributeData.Default);
        line[3] = new BufferCell("B", 1, AttributeData.Default);
        line[4] = new BufferCell("C", 1, AttributeData.Default);

        // Act
        var result = line.TranslateToString(false, 2, 5);

        // Assert
        Assert.Contains("ABC", result);
    }

    [Fact]
    public void GetTrimmedLength_ReturnsTrimmedLength()
    {
        // Arrange
        var line = new BufferLine(10);
        line[0] = new BufferCell("T", 1, AttributeData.Default);
        line[1] = new BufferCell("e", 1, AttributeData.Default);
        line[2] = new BufferCell("s", 1, AttributeData.Default);
        line[3] = new BufferCell("t", 1, AttributeData.Default);
        // Rest are whitespace/null

        // Act
        var length = line.GetTrimmedLength();

        // Assert
        Assert.Equal(4, length);
    }

    [Fact]
    public void GetTrimmedLength_EmptyLine_ReturnsZero()
    {
        // Arrange
        var line = new BufferLine(10);

        // Act
        var length = line.GetTrimmedLength();

        // Assert
        Assert.Equal(0, length);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var line = new BufferLine(10);
        line[0] = new BufferCell("A", 1, AttributeData.Default);
        line[1] = new BufferCell("B", 1, AttributeData.Default);
        line.IsWrapped = true;

        // Act
        var clone = line.Clone();

        // Assert
        Assert.Equal(line.Length, clone.Length);
        Assert.Equal(line.IsWrapped, clone.IsWrapped);
        Assert.Equal("A", clone[0].Content);
        Assert.Equal("B", clone[1].Content);
        
        // Verify independence
        clone[0] = new BufferCell("Z", 1, AttributeData.Default);
        Assert.Equal("A", line[0].Content);
        Assert.Equal("Z", clone[0].Content);
    }

    [Fact]
    public void CopyFrom_CopiesEntireLine()
    {
        // Arrange
        var srcLine = new BufferLine(10);
        srcLine[0] = new BufferCell("X", 1, AttributeData.Default);
        srcLine[1] = new BufferCell("Y", 1, AttributeData.Default);
        srcLine.IsWrapped = true;

        var destLine = new BufferLine(10);

        // Act
        destLine.CopyFrom(srcLine);

        // Assert
        Assert.Equal(srcLine.Length, destLine.Length);
        Assert.Equal(srcLine.IsWrapped, destLine.IsWrapped);
        Assert.Equal("X", destLine[0].Content);
        Assert.Equal("Y", destLine[1].Content);
    }

    [Fact]
    public void CopyFrom_DifferentSize_ResizesAndCopies()
    {
        // Arrange
        var srcLine = new BufferLine(20);
        srcLine[0] = new BufferCell("M", 1, AttributeData.Default);

        var destLine = new BufferLine(10);

        // Act
        destLine.CopyFrom(srcLine);

        // Assert
        Assert.Equal(20, destLine.Length);
        Assert.Equal("M", destLine[0].Content);
    }

    [Fact]
    public void IsWrapped_CanBeSetAndGet()
    {
        // Arrange
        var line = new BufferLine(10);

        // Act
        line.IsWrapped = true;

        // Assert
        Assert.True(line.IsWrapped);

        // Act
        line.IsWrapped = false;

        // Assert
        Assert.False(line.IsWrapped);
    }

    [Fact]
    public void LineAttribute_DefaultsToNormal()
    {
        // Arrange & Act
        var line = new BufferLine(10);

        // Assert
        Assert.Equal(LineAttribute.Normal, line.LineAttribute);
        Assert.False(line.IsDoubleWidth);
    }

    [Fact]
    public void LineAttribute_CanBeSetToDoubleWidth()
    {
        // Arrange
        var line = new BufferLine(10);

        // Act
        line.LineAttribute = LineAttribute.DoubleWidth;

        // Assert
        Assert.Equal(LineAttribute.DoubleWidth, line.LineAttribute);
        Assert.True(line.IsDoubleWidth);
    }

    [Fact]
    public void LineAttribute_DoubleHeightTop_IsDoubleWidth()
    {
        // Arrange
        var line = new BufferLine(10);

        // Act
        line.LineAttribute = LineAttribute.DoubleHeightTop;

        // Assert
        Assert.Equal(LineAttribute.DoubleHeightTop, line.LineAttribute);
        Assert.True(line.IsDoubleWidth);
    }

    [Fact]
    public void LineAttribute_DoubleHeightBottom_IsDoubleWidth()
    {
        // Arrange
        var line = new BufferLine(10);

        // Act
        line.LineAttribute = LineAttribute.DoubleHeightBottom;

        // Assert
        Assert.Equal(LineAttribute.DoubleHeightBottom, line.LineAttribute);
        Assert.True(line.IsDoubleWidth);
    }

    [Fact]
    public void Clone_PreservesLineAttribute()
    {
        // Arrange
        var line = new BufferLine(10);
        line.LineAttribute = LineAttribute.DoubleWidth;

        // Act
        var clone = line.Clone();

        // Assert
        Assert.Equal(LineAttribute.DoubleWidth, clone.LineAttribute);
    }

    [Fact]
    public void CopyFrom_PreservesLineAttribute()
    {
        // Arrange
        var srcLine = new BufferLine(10);
        srcLine.LineAttribute = LineAttribute.DoubleHeightTop;

        var destLine = new BufferLine(10);

        // Act
        destLine.CopyFrom(srcLine);

        // Assert
        Assert.Equal(LineAttribute.DoubleHeightTop, destLine.LineAttribute);
    }

    [Fact]
    public void LineAttribute_SetClearsCache()
    {
        // Arrange
        var line = new BufferLine(10);
        line.Cache = new object();

        // Act
        line.LineAttribute = LineAttribute.DoubleWidth;

        // Assert
        Assert.Null(line.Cache);
    }
}
