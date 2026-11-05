using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class WebApiCallPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "url {tag}",
            RequestPayloadTemplate = "payload {tag}",
            HttpMethod = "method {tag}",
            ContentType = "content {tag}",
            AcceptHeader = "accept {tag}",
        };

        // Act
        var state = new WebApiCallPropertiesExecutionState(props);

        // Assert
        Assert.Equal("url {tag}", state.Url);
        Assert.Equal("payload {tag}", state.RequestPayload);
        Assert.Equal("method {tag}", state.HttpMethod);
        Assert.Equal("content {tag}", state.ContentType);
        Assert.Equal("accept {tag}", state.AcceptHeader);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "prefix {tag} suffix",
            RequestPayloadTemplate = "prefix {tag} suffix",
            HttpMethod = "prefix {tag} suffix",
            ContentType = "prefix {tag} suffix",
            AcceptHeader = "prefix {tag} suffix",
        };
        var state = new WebApiCallPropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.Url);
        Assert.Equal("prefix resolved suffix", state.RequestPayload);
        Assert.Equal("prefix resolved suffix", state.HttpMethod);
        Assert.Equal("prefix resolved suffix", state.ContentType);
        Assert.Equal("prefix resolved suffix", state.AcceptHeader);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange
        var props = new WebApiCallProperties();
        var state = new WebApiCallPropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Null(state.Url);
        Assert.Null(state.RequestPayload);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new WebApiCallProperties { UrlTemplate = "prefix {tag} suffix" };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.Url);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new WebApiCallProperties { UrlTemplate = "prefix {tag} suffix" };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.Url);
    }

    [Fact]
    public void SetExecutionState_UrlEncodesSubstitutedValues_AndPreservesTemplateLiterals()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/search?query={Counterparty.Name}&apikey=secret",
        };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?>
        {
            { "Counterparty.Name", "Parent & Child / Sons Ltd." },
        });

        // Assert — substituted value encoded, template's own ?, & and = untouched
        Assert.Equal(
            "https://api.example.com/search?query=Parent%20%26%20Child%20%2F%20Sons%20Ltd.&apikey=secret",
            props.ExecutionState!.Url);
    }

    [Fact]
    public void SetExecutionState_UrlEncodesValueInPathSegment()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.parallel.ai/v1/tasks/runs/{Start.run_id}/result",
        };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?>
        {
            { "Start.run_id", "abc 123/456?x" },
        });

        // Assert
        Assert.Equal(
            "https://api.parallel.ai/v1/tasks/runs/abc%20123%2F456%3Fx/result",
            props.ExecutionState!.Url);
    }

    [Fact]
    public void SetExecutionState_UrlEncodingDoesNotLeakIntoPayloadOrHeaders()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/?q={tag}",
            RequestPayloadTemplate = """{"query":"{tag}"}""",
            AdditionalHeaders = new Dictionary<string, string> { { "X-Query", "{tag}" } },
        };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "A & B" } });

        // Assert — the URL percent-encodes; payload and headers get the un-percent-encoded value
        Assert.Equal("https://api.example.com/?q=A%20%26%20B", props.ExecutionState!.Url);
        Assert.Equal("""{"query":"A & B"}""", props.ExecutionState.RequestPayload);
        Assert.Equal("A & B", props.ExecutionState.AdditionalHeaders["X-Query"]);
    }

    [Fact]
    public void SetExecutionState_JsonEscapesSubstitutedPayloadValues()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            RequestPayloadTemplate = """{"query":"{tag}"}""",
        };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?>
        {
            { "tag", "say \"hi\"\nsecond line" },
        });

        // Assert — quotes and newline escaped so the payload stays valid JSON
        Assert.Equal(
            """{"query":"say \"hi\"\nsecond line"}""",
            props.ExecutionState!.RequestPayload);
    }

    [Fact]
    public void SetExecutionState_WholePayloadTemplateIsSinglePlaceholder_SubstitutesVerbatim()
    {
        // Arrange
        var props = new WebApiCallProperties { RequestPayloadTemplate = "{Body.json}" };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?>
        {
            { "Body.json", """{"a": 1}""" },
        });

        // Assert
        Assert.Equal("""{"a": 1}""", props.ExecutionState!.RequestPayload);
    }

    [Fact]
    public void SetExecutionState_PreEscapedPayloadValue_SubstitutedVerbatim_AndRecordsWarning()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            RequestPayloadTemplate = """{"query":"{tag}"}""",
        };

        // Act — value shaped like escape-block output: only escape sequences, no raw specials
        props.SetExecutionState(true, new Dictionary<string, string?>
        {
            { "tag", "line1\\nline2 \\\"quoted\\\"" },
        });

        // Assert
        Assert.Equal(
            """{"query":"line1\nline2 \"quoted\""}""",
            props.ExecutionState!.RequestPayload);
        string warning = Assert.Single(props.ExecutionState.SanitizationWarnings);
        Assert.Contains(nameof(WebApiCallPropertiesExecutionState.RequestPayload), warning);

        // Act again with a clean value — fresh state resets the warnings
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "plain" } });

        // Assert
        Assert.Empty(props.ExecutionState!.SanitizationWarnings);
    }

    [Fact]
    public void SetExecutionState_HeaderValueWithControlCharacters_Throws()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string> { { "X-Note", "{note}" } },
        };

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() =>
            props.SetExecutionState(true, new Dictionary<string, string?>
            {
                { "note", "line1\r\nline2" },
            }));

        // Assert
        Assert.Contains(nameof(WebApiCallPropertiesExecutionState.AdditionalHeaders), ex.Message);
        Assert.Contains("X-Note", ex.Message);
    }

    [Fact]
    public void SetExecutionState_WholeTemplateIsSinglePlaceholder_SubstitutesUrlVerbatim()
    {
        // Arrange
        var props = new WebApiCallProperties { UrlTemplate = "{NextPage.url}" };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?>
        {
            { "NextPage.url", "https://api.example.com/items?page=2&size=10" },
        });

        // Assert
        Assert.Equal("https://api.example.com/items?page=2&size=10", props.ExecutionState!.Url);
    }

    [Fact]
    public void SetExecutionState_UrlEncodesSubstitutedValuesInPollCompletionUrl()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.parallel.ai/v1/tasks/runs/{Start.run_id}",
            PollCompletionUrlTemplate = "https://api.parallel.ai/v1/tasks/runs/{Start.run_id}/result",
        };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?>
        {
            { "Start.run_id", "abc 123/456" },
        });

        // Assert
        Assert.Equal(
            "https://api.parallel.ai/v1/tasks/runs/abc%20123%2F456/result",
            props.ExecutionState!.PollCompletionUrl);
    }

    [Fact]
    public void UpdateExecutionState_UrlEncodesSubstitutedValues()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/?a={first}&b={second}",
        };
        props.SetExecutionState(true, new Dictionary<string, string?> { { "first", "one two" } });

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "second", "x&y" } });

        // Assert
        Assert.Equal("https://api.example.com/?a=one%20two&b=x%26y", props.ExecutionState!.Url);
    }

    [Fact]
    public void Constructor_CopiesAdditionalHeadersAsDefensiveCopy()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string>
            {
                { "X-Auth", "Bearer {tag}" },
                { "X-Trace", "value with {tag}" }
            }
        };

        // Act
        var state = new WebApiCallPropertiesExecutionState(props);

        // Assert
        Assert.Equal("Bearer {tag}", state.AdditionalHeaders["X-Auth"]);
        Assert.Equal("value with {tag}", state.AdditionalHeaders["X-Trace"]);

        // Mutate source — state should be unaffected
        props.AdditionalHeaders["X-Auth"] = "modified";
        Assert.Equal("Bearer {tag}", state.AdditionalHeaders["X-Auth"]);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAdditionalHeadersValues_NotKeys()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string>
            {
                { "X-Auth", "Bearer {tag}" },
                { "X-{tag}", "no replacement in key" }
            }
        };
        var state = new WebApiCallPropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert — values replaced, keys untouched
        Assert.Equal("Bearer resolved", state.AdditionalHeaders["X-Auth"]);
        Assert.True(state.AdditionalHeaders.ContainsKey("X-{tag}"));
        Assert.Equal("no replacement in key", state.AdditionalHeaders["X-{tag}"]);
    }
}
