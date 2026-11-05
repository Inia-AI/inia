using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.OutputFormat;

namespace IniaAi.Domain.Tests.OutputFormat;

public class OutputFormatValidatorTests
{
    [Fact]
    public void None_AlwaysValid()
    {
        AiQueryProperties props = new() { OutputFormatType = OutputFormatType.None };
        ValidationResult result = OutputFormatValidator.Validate("anything", props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void SingleTag_TagPresent_Valid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "answer",
        };
        ValidationResult result = OutputFormatValidator.Validate("hi <answer>yes</answer>", props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void SingleTag_TagMissing_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "answer",
        };
        ValidationResult result = OutputFormatValidator.Validate("no tags here", props);
        Assert.False(result.IsValid);
        Assert.Contains("answer", result.Error);
    }

    [Fact]
    public void SingleTag_EmptyContent_Valid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "answer",
        };
        ValidationResult result = OutputFormatValidator.Validate("<answer></answer>", props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void SingleTag_MultipleMatches_Valid_FirstWins()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "answer",
        };
        ValidationResult result = OutputFormatValidator.Validate("<answer>1</answer><answer>2</answer>", props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void SingleTag_InvalidTagName_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "1notallowed",
        };
        ValidationResult result = OutputFormatValidator.Validate("<1notallowed>x</1notallowed>", props);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void MultiTag_AllPresentAnyOrder_Valid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.MultiTag,
            OutputFormatTagNames = "thinking, answer",
        };
        ValidationResult result = OutputFormatValidator.Validate(
            "<answer>yes</answer> some text <thinking>...</thinking>",
            props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void MultiTag_OneMissing_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.MultiTag,
            OutputFormatTagNames = "thinking,answer,sources",
        };
        ValidationResult result = OutputFormatValidator.Validate(
            "<answer>yes</answer><thinking>...</thinking>",
            props);
        Assert.False(result.IsValid);
        Assert.Contains("sources", result.Error);
    }

    [Fact]
    public void Json_Parseable_Valid()
    {
        AiQueryProperties props = new() { OutputFormatType = OutputFormatType.Json };
        ValidationResult result = OutputFormatValidator.Validate("{\"a\":1}", props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Json_Malformed_Invalid()
    {
        AiQueryProperties props = new() { OutputFormatType = OutputFormatType.Json };
        ValidationResult result = OutputFormatValidator.Validate("{not json", props);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Json_WithSchemaMatch_Valid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
            OutputFormatJsonSchema = "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}",
        };
        ValidationResult result = OutputFormatValidator.Validate("{\"name\":\"alice\"}", props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Json_WithSchemaMissingRequired_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
            OutputFormatJsonSchema = "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}",
        };
        ValidationResult result = OutputFormatValidator.Validate("{\"other\":1}", props);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Json_WithSchemaExtraKeys_Valid_LenientByDefault()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
            OutputFormatJsonSchema = "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}",
        };
        ValidationResult result = OutputFormatValidator.Validate("{\"name\":\"a\",\"extra\":42}", props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Json_TrailingCommaAcceptedByTheJsonParser_StillCheckedAgainstSchema()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
            OutputFormatJsonSchema = "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}",
        };

        ValidationResult result = OutputFormatValidator.Validate("{\"name\":\"alice\",}", props);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Json_TrailingCommaWithSchemaViolation_ReportsViolationRatherThanThrowing()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
            OutputFormatJsonSchema = "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}",
        };

        ValidationResult result = OutputFormatValidator.Validate("{\"other\":1,}", props);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Json_BadUserSchema_InvalidWithMessage()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
            OutputFormatJsonSchema = "{not a schema",
        };
        ValidationResult result = OutputFormatValidator.Validate("{\"a\":1}", props);
        Assert.False(result.IsValid);
        Assert.Contains("Schema", result.Error);
    }

    [Fact]
    public void JsonInTag_TagPresentInnerJsonValid_Valid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
        };
        ValidationResult result = OutputFormatValidator.Validate(
            "<answer>{\"a\":1}</answer>",
            props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void JsonInTag_InnerCodeFenced_StillValid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
        };
        string raw = "<answer>```json\n{\"a\":1}\n```</answer>";
        ValidationResult result = OutputFormatValidator.Validate(raw, props);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void JsonInTag_TagMissing_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
        };
        ValidationResult result = OutputFormatValidator.Validate("{\"a\":1}", props);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void JsonInTag_InnerNotJson_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
        };
        ValidationResult result = OutputFormatValidator.Validate("<answer>not json</answer>", props);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void JsonInTag_InvalidTagName_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "1bad",
        };
        ValidationResult result = OutputFormatValidator.Validate("<1bad>{\"a\":1}</1bad>", props);
        Assert.False(result.IsValid);
        Assert.Contains("Tag name", result.Error);
    }

    [Fact]
    public void JsonInTag_InnerJsonSchemaMismatch_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
            OutputFormatJsonSchema = "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}",
        };
        ValidationResult result = OutputFormatValidator.Validate("<answer>{\"other\":1}</answer>", props);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void MultiTag_EmptyCsv_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.MultiTag,
            OutputFormatTagNames = "",
        };
        ValidationResult result = OutputFormatValidator.Validate("<x>y</x>", props);
        Assert.False(result.IsValid);
        Assert.Contains("at least one tag name", result.Error);
    }

    [Fact]
    public void MultiTag_NullCsv_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.MultiTag,
            OutputFormatTagNames = null,
        };
        ValidationResult result = OutputFormatValidator.Validate("<x>y</x>", props);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void MultiTag_InvalidTagInList_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.MultiTag,
            OutputFormatTagNames = "answer,1bad",
        };
        ValidationResult result = OutputFormatValidator.Validate("<answer>x</answer><1bad>y</1bad>", props);
        Assert.False(result.IsValid);
        Assert.Contains("1bad", result.Error);
    }

    [Fact]
    public void UnsupportedFormatType_Invalid()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = (OutputFormatType)999,
        };
        ValidationResult result = OutputFormatValidator.Validate("anything", props);
        Assert.False(result.IsValid);
        Assert.Contains("Unsupported", result.Error);
    }
}
