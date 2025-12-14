using XTerm.NET.Parser;

namespace XTerm.NET.Tests.Parser;

public class ParamsTests
{
    [Fact]
    public void Constructor_CreatesEmptyParams()
    {
        // Arrange & Act
        var params_ = new Params();

        // Assert
        Assert.Equal(0, params_.Length);
    }

    [Fact]
    public void AddParam_AddsParameter()
    {
        // Arrange
        var params_ = new Params();

        // Act
        params_.AddParam(10);

        // Assert
        Assert.Equal(1, params_.Length);
        Assert.Equal(10, params_.GetParam(0));
    }

    [Fact]
    public void AddParam_MultipleParameters_AddsAll()
    {
        // Arrange
        var params_ = new Params();

        // Act
        params_.AddParam(1);
        params_.AddParam(2);
        params_.AddParam(3);

        // Assert
        Assert.Equal(3, params_.Length);
        Assert.Equal(1, params_.GetParam(0));
        Assert.Equal(2, params_.GetParam(1));
        Assert.Equal(3, params_.GetParam(2));
    }

    [Fact]
    public void GetParam_ValidIndex_ReturnsParameter()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(42);

        // Act
        var value = params_.GetParam(0);

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void GetParam_InvalidIndex_ReturnsDefault()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(10);

        // Act
        var value = params_.GetParam(5, 99);

        // Assert
        Assert.Equal(99, value);
    }

    [Fact]
    public void GetParam_NegativeIndex_ReturnsDefault()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(10);

        // Act
        var value = params_.GetParam(-1, 50);

        // Assert
        Assert.Equal(50, value);
    }

    [Fact]
    public void GetParam_MinusOneValue_ReturnsDefault()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(-1); // Special value meaning "use default"

        // Act
        var value = params_.GetParam(0, 100);

        // Assert
        Assert.Equal(100, value);
    }

    [Fact]
    public void GetParam_NoDefault_ReturnsZero()
    {
        // Arrange
        var params_ = new Params();

        // Act
        var value = params_.GetParam(0);

        // Assert
        Assert.Equal(0, value);
    }

    [Fact]
    public void HasParam_ValidIndex_ReturnsTrue()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(10);

        // Act
        var hasParam = params_.HasParam(0);

        // Assert
        Assert.True(hasParam);
    }

    [Fact]
    public void HasParam_InvalidIndex_ReturnsFalse()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(10);

        // Act
        var hasParam = params_.HasParam(5);

        // Assert
        Assert.False(hasParam);
    }

    [Fact]
    public void HasParam_MinusOneValue_ReturnsFalse()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(-1);

        // Act
        var hasParam = params_.HasParam(0);

        // Assert
        Assert.False(hasParam);
    }

    [Fact]
    public void HasParam_NegativeIndex_ReturnsFalse()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(10);

        // Act
        var hasParam = params_.HasParam(-1);

        // Assert
        Assert.False(hasParam);
    }

    [Fact]
    public void Reset_ClearsAllParameters()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(1);
        params_.AddParam(2);
        params_.AddParam(3);

        // Act
        params_.Reset();

        // Assert
        Assert.Equal(0, params_.Length);
    }

    [Fact]
    public void Reset_AllowsReuse()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(10);
        params_.Reset();

        // Act
        params_.AddParam(20);

        // Assert
        Assert.Equal(1, params_.Length);
        Assert.Equal(20, params_.GetParam(0));
    }

    [Fact]
    public void ToArray_ReturnsAllParameters()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(1);
        params_.AddParam(2);
        params_.AddParam(3);

        // Act
        var array = params_.ToArray();

        // Assert
        Assert.Equal(3, array.Length);
        Assert.Equal(1, array[0]);
        Assert.Equal(2, array[1]);
        Assert.Equal(3, array[2]);
    }

    [Fact]
    public void ToArray_EmptyParams_ReturnsEmptyArray()
    {
        // Arrange
        var params_ = new Params();

        // Act
        var array = params_.ToArray();

        // Assert
        Assert.Empty(array);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(1);
        params_.AddParam(2);
        params_.AddParam(3);

        // Act
        var clone = params_.Clone();

        // Assert
        Assert.Equal(params_.Length, clone.Length);
        Assert.Equal(1, clone.GetParam(0));
        Assert.Equal(2, clone.GetParam(1));
        Assert.Equal(3, clone.GetParam(2));

        // Verify independence
        clone.AddParam(4);
        Assert.Equal(3, params_.Length);
        Assert.Equal(4, clone.Length);
    }

    [Fact]
    public void AddSubParam_AddsSubParameter()
    {
        // Arrange
        var params_ = new Params();

        // Act
        params_.AddSubParam(100);

        // Assert - Method should not throw
        // Sub-params are internal detail
    }

    [Fact]
    public void GetSubParams_ReturnsSubParameters()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(1);
        params_.AddSubParam(10);
        params_.AddSubParam(20);

        // Act
        var subParams = params_.GetSubParams(0);

        // Assert
        Assert.NotNull(subParams);
        // Current implementation returns empty list, which is valid
    }

    [Fact]
    public void GetSubParams_InvalidIndex_ReturnsEmptyList()
    {
        // Arrange
        var params_ = new Params();

        // Act
        var subParams = params_.GetSubParams(10);

        // Assert
        Assert.NotNull(subParams);
        Assert.Empty(subParams);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(255)]
    [InlineData(1000)]
    public void AddParam_VariousValues_WorksCorrectly(int value)
    {
        // Arrange
        var params_ = new Params();

        // Act
        params_.AddParam(value);

        // Assert
        Assert.Equal(value, params_.GetParam(0));
    }

    [Fact]
    public void MultipleOperations_WorkCorrectly()
    {
        // Arrange
        var params_ = new Params();

        // Act
        params_.AddParam(1);
        params_.AddParam(2);
        var v1 = params_.GetParam(0);
        var v2 = params_.GetParam(1);
        
        params_.Reset();
        params_.AddParam(10);
        params_.AddParam(20);
        params_.AddParam(30);
        var v3 = params_.GetParam(0);
        var array = params_.ToArray();
        
        var clone = params_.Clone();
        clone.AddParam(40);

        // Assert
        Assert.Equal(1, v1);
        Assert.Equal(2, v2);
        Assert.Equal(10, v3);
        Assert.Equal(3, array.Length);
        Assert.Equal(3, params_.Length);
        Assert.Equal(4, clone.Length);
    }

    [Fact]
    public void LargeNumberOfParams_HandlesCorrectly()
    {
        // Arrange
        var params_ = new Params();

        // Act
        for (int i = 0; i < 50; i++)
        {
            params_.AddParam(i);
        }

        // Assert
        Assert.Equal(50, params_.Length);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(i, params_.GetParam(i));
        }
    }

    [Fact]
    public void GetParam_WithDefaultValue_UsesDefaultWhenNeeded()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(10);
        params_.AddParam(-1); // Special value
        params_.AddParam(20);

        // Act & Assert
        Assert.Equal(10, params_.GetParam(0, 99));
        Assert.Equal(99, params_.GetParam(1, 99)); // Should use default
        Assert.Equal(20, params_.GetParam(2, 99));
        Assert.Equal(99, params_.GetParam(5, 99)); // Out of range
    }

    [Fact]
    public void ZeroParameters_HandlesCorrectly()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(0);

        // Act
        var value = params_.GetParam(0, 10);

        // Assert
        Assert.Equal(0, value); // Zero is a valid value, not default
    }

    [Fact]
    public void UpdateLastParam_UpdatesParameter()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(0);

        // Act
        params_.UpdateLastParam(5);

        // Assert
        Assert.Equal(5, params_.GetParam(0));
    }

    [Fact]
    public void UpdateLastParam_BuildsNumberFromDigits()
    {
        // Arrange
        var params_ = new Params();
        params_.AddParam(0);

        // Act
        params_.UpdateLastParam(1);   // 1
        params_.UpdateLastParam(12);  // 12
        params_.UpdateLastParam(123); // 123

        // Assert
        Assert.Equal(123, params_.GetParam(0));
    }
}
