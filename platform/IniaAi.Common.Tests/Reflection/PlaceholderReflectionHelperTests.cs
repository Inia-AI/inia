using IniaAi.Common.Reflection;

namespace IniaAi.Common.Tests.Reflection;

// Test POCOs — deliberately simple, no domain dependencies
public class SimpleTarget
{
    public string? Name { get; set; }
    public string? Title { get; set; }
    public int Count { get; set; }
    public bool Flag { get; set; }
}

public class WithCollections
{
    public string? Label { get; set; }
    public Dictionary<string, string> Tags { get; set; } = [];
    public List<string> Items { get; set; } = [];
}

public class WithUrlProperty
{
    [SanitizePlaceholderValues(PlaceholderSanitization.Url)]
    public string? Url { get; set; }

    public string? Body { get; set; }
}

public class WithJsonProperty
{
    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    public string? Payload { get; set; }

    public string? Body { get; set; }
}

public class WithHeaderValueProperty
{
    [SanitizePlaceholderValues(PlaceholderSanitization.HeaderValue)]
    public string? Accept { get; set; }
}

public class JsonSinkTarget : ISanitizationWarningSink
{
    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    public string? Payload { get; set; }

    [PlaceholderReplacementIgnore]
    public List<string> Warnings { get; } = [];

    public void AddSanitizationWarning(string warning)
    {
        Warnings.Add(warning);
    }
}

public class WithSanitizedCollections
{
    [SanitizePlaceholderValues(PlaceholderSanitization.HeaderValue)]
    public Dictionary<string, string> Headers { get; set; } = [];

    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    public List<string> JsonInputs { get; set; } = [];

    [PlaceholderReplacementIgnore]
    public List<string> IgnoredItems { get; set; } = [];
}

public class PlaceholderReflectionHelperTests
{
    #region ReplaceAll

    [Fact]
    public void ReplaceAll_ReplacesStringProperties()
    {
        // Arrange
        var target = new SimpleTarget { Name = "Hello {tag}", Title = "World {tag}" };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "tag", "resolved" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Hello resolved", target.Name);
        Assert.Equal("World resolved", target.Title);
    }

    [Fact]
    public void ReplaceAll_ReplacesListItems()
    {
        // Arrange
        var target = new WithCollections { Items = ["prefix {tag} suffix", "no placeholder"] };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "tag", "resolved" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("prefix resolved suffix", target.Items[0]);
        Assert.Equal("no placeholder", target.Items[1]);
    }

    [Fact]
    public void ReplaceAll_ReplacesDictValues_NotKeys()
    {
        // Arrange
        var target = new WithCollections
        {
            Tags = new Dictionary<string, string>
            {
                { "{tag}", "value with {tag}" },
                { "other", "no placeholder" }
            }
        };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "tag", "resolved" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert — keys unchanged, values replaced
        Assert.True(target.Tags.ContainsKey("{tag}"));
        Assert.Equal("value with resolved", target.Tags["{tag}"]);
        Assert.Equal("no placeholder", target.Tags["other"]);
    }

    [Fact]
    public void ReplaceAll_SkipsNonStringProperties()
    {
        // Arrange
        var target = new SimpleTarget { Name = "{tag}", Count = 42, Flag = true };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "tag", "resolved" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("resolved", target.Name);
        Assert.Equal(42, target.Count);
        Assert.True(target.Flag);
    }

    [Fact]
    public void ReplaceAll_UrlEncodedProperty_EncodesSubstitutedValues_OthersStayRaw()
    {
        // Arrange
        var target = new WithUrlProperty
        {
            Url = "https://api.example.com/?q={tag}&key=abc",
            Body = "query is {tag}",
        };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "tag", "A & B" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://api.example.com/?q=A%20%26%20B&key=abc", target.Url);
        Assert.Equal("query is A & B", target.Body);
    }

    [Fact]
    public void ReplaceAll_UrlEncodedProperty_WholePropertyIsSinglePlaceholder_SubstitutesVerbatim()
    {
        // Arrange
        var target = new WithUrlProperty { Url = "{next}" };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "next", "https://x.com/a?b=1&c=2" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://x.com/a?b=1&c=2", target.Url);
    }

    [Fact]
    public void ReplaceAll_JsonProperty_EscapesSubstitutedValues_OthersStayRaw()
    {
        // Arrange
        var target = new WithJsonProperty
        {
            Payload = "{\"query\": \"{tag}\"}",
            Body = "query is {tag}",
        };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "tag", "say \"hi\"\nbye" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"query\": \"say \\\"hi\\\"\\nbye\"}", target.Payload);
        Assert.Equal("query is say \"hi\"\nbye", target.Body);
    }

    [Fact]
    public void ReplaceAll_JsonProperty_WholePropertyIsSinglePlaceholder_SubstitutesVerbatim()
    {
        // Arrange
        var target = new WithJsonProperty { Payload = "{doc.json}" };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "doc.json", "{\"a\": 1}" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"a\": 1}", target.Payload);
    }

    [Fact]
    public void ReplaceAll_HeaderValueProperty_CleanValue_Passes()
    {
        // Arrange
        var target = new WithHeaderValueProperty { Accept = "application/{format}" };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "format", "json" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("application/json", target.Accept);
    }

    [Fact]
    public void ReplaceAll_HeaderValueProperty_ControlCharacter_ThrowsNamingProperty()
    {
        // Arrange
        var target = new WithHeaderValueProperty { Accept = "{value}" };

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PlaceholderReflectionHelper.ReplaceAll(target, true,
                new Dictionary<string, string?> { { "value", "text/plain\r\nX-Injected: 1" } },
                StringComparison.InvariantCultureIgnoreCase));

        // Assert
        Assert.Contains(nameof(WithHeaderValueProperty.Accept), ex.Message);
    }

    [Fact]
    public void ReplaceAll_HeaderValueProperty_DelCharacter_Throws()
    {
        // Arrange
        var target = new WithHeaderValueProperty { Accept = "{value}" };

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PlaceholderReflectionHelper.ReplaceAll(target, true,
                new Dictionary<string, string?> { { "value", "text/plain" + (char)0x7F + "del" } },
                StringComparison.InvariantCultureIgnoreCase));

        // Assert
        Assert.Contains(nameof(WithHeaderValueProperty.Accept), ex.Message);
    }

    [Fact]
    public void ReplaceAll_HeaderValueDict_CleanValues_Replaced()
    {
        // Arrange
        var target = new WithSanitizedCollections
        {
            Headers = new Dictionary<string, string> { { "Authorization", "Bearer {token}" } }
        };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "token", "abc123" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Bearer abc123", target.Headers["Authorization"]);
    }

    [Fact]
    public void ReplaceAll_HeaderValueDict_ControlCharacter_ThrowsNamingPropertyAndKey()
    {
        // Arrange
        var target = new WithSanitizedCollections
        {
            Headers = new Dictionary<string, string> { { "X-Note", "{note}" } }
        };

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PlaceholderReflectionHelper.ReplaceAll(target, true,
                new Dictionary<string, string?> { { "note", "line1\r\nline2" } },
                StringComparison.InvariantCultureIgnoreCase));

        // Assert
        Assert.Contains(nameof(WithSanitizedCollections.Headers), ex.Message);
        Assert.Contains("X-Note", ex.Message);
    }

    [Fact]
    public void ReplaceAll_JsonList_EscapesEmbeddedValues_WholePlaceholderItemsVerbatim()
    {
        // Arrange
        var target = new WithSanitizedCollections
        {
            JsonInputs =
            [
                "{\"note\": \"{comment}\"}",
                "{doc.json}",
            ]
        };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?>
            {
                { "comment", "say \"hi\"" },
                { "doc.json", "{\"a\": 1}" },
            },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"note\": \"say \\\"hi\\\"\"}", target.JsonInputs[0]);
        Assert.Equal("{\"a\": 1}", target.JsonInputs[1]);
    }

    [Fact]
    public void ReplaceAll_JsonProperty_PreEscapedValue_RecordsSanitizationWarning()
    {
        // Arrange
        var target = new JsonSinkTarget { Payload = "{\"content\": \"{msg}\"}" };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "msg", "pre \\n escaped" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert — value substituted verbatim, skip reported with property and placeholder
        Assert.Equal("{\"content\": \"pre \\n escaped\"}", target.Payload);
        string warning = Assert.Single(target.Warnings);
        Assert.Contains(nameof(JsonSinkTarget.Payload), warning);
        Assert.Contains("msg", warning);
    }

    [Fact]
    public void ReplaceAll_JsonProperty_RawValue_NoSanitizationWarning()
    {
        // Arrange
        var target = new JsonSinkTarget { Payload = "{\"content\": \"{msg}\"}" };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "msg", "plain \"text\"" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"content\": \"plain \\\"text\\\"\"}", target.Payload);
        Assert.Empty(target.Warnings);
    }

    [Fact]
    public void ReplaceAll_ListWithIgnoreAttribute_IsNotReplaced()
    {
        // Arrange
        var target = new WithSanitizedCollections { IgnoredItems = ["keep {tag} as-is"] };

        // Act
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "tag", "resolved" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("keep {tag} as-is", target.IgnoredItems[0]);
    }

    [Fact]
    public void ReplaceAll_NullPropertyValues_DoesNotThrow()
    {
        // Arrange
        var target = new SimpleTarget { Name = null, Title = null };

        // Act & Assert — should not throw
        PlaceholderReflectionHelper.ReplaceAll(target, true,
            new Dictionary<string, string?> { { "tag", "resolved" } },
            StringComparison.InvariantCultureIgnoreCase);

        Assert.Null(target.Name);
        Assert.Null(target.Title);
    }

    #endregion

    #region HasPlaceholder

    [Fact]
    public void HasPlaceholder_FindsInStringProperty()
    {
        // Arrange
        var target = new SimpleTarget { Name = "Hello {tag}" };

        // Act & Assert
        Assert.True(PlaceholderReflectionHelper.HasPlaceholder(target, "{tag}"));
    }

    [Fact]
    public void HasPlaceholder_FindsInListItem()
    {
        // Arrange
        var target = new WithCollections { Items = ["item with {tag}"] };

        // Act & Assert
        Assert.True(PlaceholderReflectionHelper.HasPlaceholder(target, "{tag}"));
    }

    [Fact]
    public void HasPlaceholder_FindsInDictValue()
    {
        // Arrange
        var target = new WithCollections
        {
            Tags = new Dictionary<string, string> { { "key", "value with {tag}" } }
        };

        // Act & Assert
        Assert.True(PlaceholderReflectionHelper.HasPlaceholder(target, "{tag}"));
    }

    [Fact]
    public void HasPlaceholder_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var target = new SimpleTarget { Name = "Hello World", Title = "No placeholder" };

        // Act & Assert
        Assert.False(PlaceholderReflectionHelper.HasPlaceholder(target, "{tag}"));
    }

    #endregion

    #region GetAllPlaceholders

    [Fact]
    public void GetAllPlaceholders_ExtractsFromAllPropertyTypes()
    {
        // Arrange
        var target = new WithCollections
        {
            Label = "Hello {name}",
            Tags = new Dictionary<string, string> { { "key", "value {role}" } },
            Items = ["{item1}", "no placeholder"]
        };

        // Act
        List<string> placeholders = [.. PlaceholderReflectionHelper.GetAllPlaceholders(target)];

        // Assert
        Assert.Contains("name", placeholders);
        Assert.Contains("role", placeholders);
        Assert.Contains("item1", placeholders);
        Assert.Equal(3, placeholders.Count);
    }

    #endregion
}
