using IniaAi.Common.DataMasking;
using IniaAi.Common.Enums;

namespace IniaAi.Common.Tests;

public class StringReplacementsTests
{
    [Fact]
    public void GetReplacementString_WhenCalledWithCountryEntityType_ReturnsReplacementString()
    {
        // Arrange
        string originalString = "United States";
        NamedEntityType entityType = NamedEntityType.Country;
        string expectedReplacement = "Czech Republic";

        var stringReplacements = new StringReplacements();

        // Act
        string actualReplacement = stringReplacements.GetReplacementString(originalString, entityType);

        // Assert
        Assert.Equal(expectedReplacement, actualReplacement);
    }

    [Fact]
    public void GetReplacementString_WhenCalledWithUrlEntityType_ReturnsReplacementString()
    {
        // Arrange
        string originalString = "https://www.example.com";
        NamedEntityType entityType = NamedEntityType.Url;
        string expectedReplacement = "https://bloomberg.com";

        var stringReplacements = new StringReplacements();

        // Act
        string actualReplacement = stringReplacements.GetReplacementString(originalString, entityType);

        // Assert
        Assert.Equal(expectedReplacement, actualReplacement);
    }

    [Fact]
    public void GetReplacementString_WhenCalledWithEmailEntityType_ReturnsReplacementString()
    {
        // Arrange
        string originalString = "test@example.com";
        NamedEntityType entityType = NamedEntityType.Email;
        string expectedReplacement = "alumni@college.net";

        var stringReplacements = new StringReplacements();

        // Act
        string actualReplacement = stringReplacements.GetReplacementString(originalString, entityType);

        // Assert
        Assert.Equal(expectedReplacement, actualReplacement);
    }

    [Fact]
    public void GetReplacementString_WhenCalledWithFirstNameEntityType_ReturnsReplacementString()
    {
        // Arrange
        string originalString = "John";
        NamedEntityType entityType = NamedEntityType.Person;
        string expectedReplacement = "Ross";

        var stringReplacements = new StringReplacements();

        // Act
        string actualReplacement = stringReplacements.GetReplacementString(originalString, entityType);

        // Assert
        Assert.Equal(expectedReplacement, actualReplacement);
    }

    [Fact]
    public void GetReplacementString_WhenCalledWithLastNameEntityType_ReturnsReplacementString()
    {
        // Arrange
        string originalString = "Doe";
        NamedEntityType entityType = NamedEntityType.Person;
        string expectedReplacement = "Day";

        var stringReplacements = new StringReplacements();

        // Act
        string actualReplacement = stringReplacements.GetReplacementString(originalString, entityType);

        // Assert
        Assert.Equal(expectedReplacement, actualReplacement);
    }

    [Fact]
    public void GetReplacementString_WhenCalledWithAllEntityType_ReturnsReplacementString()
    {
        // Arrange
        string originalString = "Other";
        NamedEntityType entityType = NamedEntityType.All;
        string expectedReplacement = "Focal";

        var stringReplacements = new StringReplacements();

        // Act
        string actualReplacement = stringReplacements.GetReplacementString(originalString, entityType);

        // Assert
        Assert.Equal(expectedReplacement, actualReplacement);
    }
}
