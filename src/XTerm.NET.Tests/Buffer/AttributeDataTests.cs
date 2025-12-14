using XTerm.Buffer;

namespace XTerm.Tests.Buffer;

public class AttributeDataTests
{
    [Fact]
    public void Constructor_Default_SetsDefaultValues()
    {
        // Arrange & Act
        var attr = new AttributeData();

        // Assert
        Assert.Equal(256, attr.Fg);
        Assert.Equal(257, attr.Bg);
        Assert.Equal(0, attr.Extended);
    }

    [Fact]
    public void Constructor_WithParameters_SetsValues()
    {
        // Arrange & Act
        var attr = new AttributeData(10, 20, 5);

        // Assert
        Assert.Equal(10, attr.Fg);
        Assert.Equal(20, attr.Bg);
        Assert.Equal(5, attr.Extended);
    }

    [Fact]
    public void Default_Property_ReturnsDefaultAttributes()
    {
        // Act
        var attr = AttributeData.Default;

        // Assert
        Assert.Equal(256, attr.Fg);
        Assert.Equal(257, attr.Bg);
        Assert.Equal(0, attr.Extended);
    }

    [Fact]
    public void SetBold_True_SetsBoldFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetBold(true);

        // Assert
        Assert.True(attr.IsBold());
    }

    [Fact]
    public void SetBold_False_ClearsBoldFlag()
    {
        // Arrange
        var attr = new AttributeData();
        attr.SetBold(true);

        // Act
        attr.SetBold(false);

        // Assert
        Assert.False(attr.IsBold());
    }

    [Fact]
    public void SetDim_True_SetsDimFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetDim(true);

        // Assert
        Assert.True(attr.IsDim());
    }

    [Fact]
    public void SetItalic_True_SetsItalicFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetItalic(true);

        // Assert
        Assert.True(attr.IsItalic());
    }

    [Fact]
    public void SetUnderline_True_SetsUnderlineFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetUnderline(true);

        // Assert
        Assert.True(attr.IsUnderline());
    }

    [Fact]
    public void SetBlink_True_SetsBlinkFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetBlink(true);

        // Assert
        Assert.True(attr.IsBlink());
    }

    [Fact]
    public void SetInverse_True_SetsInverseFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetInverse(true);

        // Assert
        Assert.True(attr.IsInverse());
    }

    [Fact]
    public void SetInvisible_True_SetsInvisibleFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetInvisible(true);

        // Assert
        Assert.True(attr.IsInvisible());
    }

    [Fact]
    public void SetStrikethrough_True_SetsStrikethroughFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetStrikethrough(true);

        // Assert
        Assert.True(attr.IsStrikethrough());
    }

    [Fact]
    public void SetOverline_True_SetsOverlineFlag()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetOverline(true);

        // Assert
        Assert.True(attr.IsOverline());
    }

    [Fact]
    public void MultipleFlags_CanBeSetSimultaneously()
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetBold(true);
        attr.SetItalic(true);
        attr.SetUnderline(true);

        // Assert
        Assert.True(attr.IsBold());
        Assert.True(attr.IsItalic());
        Assert.True(attr.IsUnderline());
    }

    [Fact]
    public void SetFgColor_SetsColor()
    {
        // Arrange
        var attr = new AttributeData();
        var color = 15;

        // Act
        attr.SetFgColor(color);

        // Assert
        Assert.Equal(color, attr.GetFgColor());
    }

    [Fact]
    public void SetFgColor_WithMode_SetsColorAndMode()
    {
        // Arrange
        var attr = new AttributeData();
        var color = 0xFF0000; // Red in RGB
        var mode = 1;

        // Act
        attr.SetFgColor(color, mode);

        // Assert
        Assert.Equal(color, attr.GetFgColor());
        Assert.Equal(mode, attr.GetFgColorMode());
    }

    [Fact]
    public void SetBgColor_SetsColor()
    {
        // Arrange
        var attr = new AttributeData();
        var color = 10;

        // Act
        attr.SetBgColor(color);

        // Assert
        Assert.Equal(color, attr.GetBgColor());
    }

    [Fact]
    public void SetBgColor_WithMode_SetsColorAndMode()
    {
        // Arrange
        var attr = new AttributeData();
        var color = 0x00FF00; // Green in RGB
        var mode = 1;

        // Act
        attr.SetBgColor(color, mode);

        // Assert
        Assert.Equal(color, attr.GetBgColor());
        Assert.Equal(mode, attr.GetBgColorMode());
    }

    [Fact]
    public void Equals_SameAttributes_ReturnsTrue()
    {
        // Arrange
        var attr1 = new AttributeData(10, 20, 5);
        var attr2 = new AttributeData(10, 20, 5);

        // Act & Assert
        Assert.True(attr1.Equals(attr2));
        Assert.True(attr1 == attr2);
    }

    [Fact]
    public void Equals_DifferentFg_ReturnsFalse()
    {
        // Arrange
        var attr1 = new AttributeData(10, 20, 5);
        var attr2 = new AttributeData(15, 20, 5);

        // Act & Assert
        Assert.False(attr1.Equals(attr2));
        Assert.True(attr1 != attr2);
    }

    [Fact]
    public void Equals_DifferentBg_ReturnsFalse()
    {
        // Arrange
        var attr1 = new AttributeData(10, 20, 5);
        var attr2 = new AttributeData(10, 25, 5);

        // Act & Assert
        Assert.False(attr1.Equals(attr2));
    }

    [Fact]
    public void Equals_DifferentExtended_ReturnsFalse()
    {
        // Arrange
        var attr1 = new AttributeData(10, 20, 5);
        var attr2 = new AttributeData(10, 20, 10);

        // Act & Assert
        Assert.False(attr1.Equals(attr2));
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var attr = new AttributeData(10, 20, 0);
        attr.SetBold(true);
        attr.SetItalic(true);

        // Act
        var clone = attr.Clone();

        // Assert
        Assert.Equal(attr.Fg, clone.Fg);
        Assert.Equal(attr.Bg, clone.Bg);
        Assert.Equal(attr.Extended, clone.Extended);
        Assert.True(clone.IsBold());
        Assert.True(clone.IsItalic());
        
        // Verify it's a true copy
        clone.SetBold(false);
        Assert.True(attr.IsBold());
        Assert.False(clone.IsBold());
    }

    [Fact]
    public void GetHashCode_SameAttributes_ReturnsSameHash()
    {
        // Arrange
        var attr1 = new AttributeData(10, 20, 5);
        var attr2 = new AttributeData(10, 20, 5);

        // Act & Assert
        Assert.Equal(attr1.GetHashCode(), attr2.GetHashCode());
    }

    [Fact]
    public void AllFlags_CanBeToggled()
    {
        // Arrange
        var attr = new AttributeData();

        // Act - Set all flags
        attr.SetBold(true);
        attr.SetDim(true);
        attr.SetItalic(true);
        attr.SetUnderline(true);
        attr.SetBlink(true);
        attr.SetInverse(true);
        attr.SetInvisible(true);
        attr.SetStrikethrough(true);
        attr.SetOverline(true);

        // Assert - All should be true
        Assert.True(attr.IsBold());
        Assert.True(attr.IsDim());
        Assert.True(attr.IsItalic());
        Assert.True(attr.IsUnderline());
        Assert.True(attr.IsBlink());
        Assert.True(attr.IsInverse());
        Assert.True(attr.IsInvisible());
        Assert.True(attr.IsStrikethrough());
        Assert.True(attr.IsOverline());

        // Act - Clear all flags
        attr.SetBold(false);
        attr.SetDim(false);
        attr.SetItalic(false);
        attr.SetUnderline(false);
        attr.SetBlink(false);
        attr.SetInverse(false);
        attr.SetInvisible(false);
        attr.SetStrikethrough(false);
        attr.SetOverline(false);

        // Assert - All should be false
        Assert.False(attr.IsBold());
        Assert.False(attr.IsDim());
        Assert.False(attr.IsItalic());
        Assert.False(attr.IsUnderline());
        Assert.False(attr.IsBlink());
        Assert.False(attr.IsInverse());
        Assert.False(attr.IsInvisible());
        Assert.False(attr.IsStrikethrough());
        Assert.False(attr.IsOverline());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(255)]
    public void SetFgColor_VariousValues_WorksCorrectly(int color)
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetFgColor(color);

        // Assert
        Assert.Equal(color, attr.GetFgColor());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(255)]
    public void SetBgColor_VariousValues_WorksCorrectly(int color)
    {
        // Arrange
        var attr = new AttributeData();

        // Act
        attr.SetBgColor(color);

        // Assert
        Assert.Equal(color, attr.GetBgColor());
    }
}
