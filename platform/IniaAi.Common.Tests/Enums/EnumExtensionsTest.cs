using IniaAi.Common.Extensions;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Common.Tests.Enums;

public class EnumExtensionsTest
{
    private enum TestEnum
    {
        [Description("First Value Description")]
        FirstValue,

        [Description("Second Value Description")]
        SecondValue,

        ThirdValue
    }

    private enum TestEnumMember
    {
        [EnumMember(Value = "First_Value")]
        FirstValue,

        [EnumMember(Value = "Second_Value")]
        SecondValue,

        [EnumMember(Value = null)]
        NullValue,

        ThirdValue
    }

    [Fact]
    public void GetDescription_WithDescriptionAttribute_ReturnsDescription()
    {
        // Arrange
        TestEnum value = TestEnum.FirstValue;

        // Act
        string result = value.GetDescription();

        // Assert
        Assert.Equal("First Value Description", result);
    }

    [Fact]
    public void GetDescription_WithNoDescriptionAttribute_ReturnsEnumName()
    {
        // Arrange
        TestEnum value = TestEnum.ThirdValue;

        // Act
        string result = value.GetDescription();

        // Assert
        Assert.Equal("ThirdValue", result);
    }

    [Fact]
    public void GetDescription_WithNullValue_ReturnsEmptyString()
    {
        // Arrange
        TestEnum? value = null;

        // Act
        string result = value!.GetDescription();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetEnumMember_WithEnumMemberAttribute_ReturnsValue()
    {
        // Arrange
        TestEnumMember value = TestEnumMember.FirstValue;

        // Act
        string result = value.GetEnumMember();

        // Assert
        Assert.Equal("First_Value", result);
    }

    [Fact]
    public void GetEnumMember_WithNoEnumMemberAttribute_ReturnsEnumName()
    {
        // Arrange
        TestEnumMember value = TestEnumMember.ThirdValue;

        // Act
        string result = value.GetEnumMember();

        // Assert
        Assert.Equal("ThirdValue", result);
    }

    [Fact]
    public void GetEnumMember_WithNullValueInAttribute_ReturnsEnumName()
    {
        // Arrange
        TestEnumMember value = TestEnumMember.NullValue;

        // Act
        string result = value.GetEnumMember();

        // Assert
        Assert.Equal("NullValue", result);
    }

    [Fact]
    public void GetEnumMember_WithNullValue_ReturnsEmptyString()
    {
        // Arrange
        TestEnumMember? value = null;

        // Act
        string result = value!.GetEnumMember();

        // Assert
        Assert.Equal(string.Empty, result);
    }
}
