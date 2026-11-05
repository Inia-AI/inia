using IniaAi.Common.Reflection;

namespace IniaAi.Common.Tests.Reflection;

public class SanitizePlaceholderValuesAttributeTests
{
    [Fact]
    public void Constructor_SingleMode_SetsMode()
    {
        var attribute = new SanitizePlaceholderValuesAttribute(PlaceholderSanitization.Json);

        Assert.Equal(PlaceholderSanitization.Json, attribute.Mode);
    }

    [Fact]
    public void Constructor_CombinedModes_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new SanitizePlaceholderValuesAttribute(PlaceholderSanitization.Url | PlaceholderSanitization.Json));

        Assert.Contains("combined", ex.Message);
    }
}
