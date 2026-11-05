using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.OutputFormat;

namespace IniaAi.Domain.Tests.OutputFormat;

public class OutputFormatExtractorTests
{
    [Fact]
    public void None_ReturnsEmpty()
    {
        AiQueryProperties props = new() { OutputFormatType = OutputFormatType.None };
        Dictionary<string, string> result = OutputFormatExtractor.Extract("anything", props);
        Assert.Empty(result);
    }

    [Fact]
    public void SingleTag_ReturnsInnerText()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "answer",
        };
        Dictionary<string, string> result = OutputFormatExtractor.Extract("<answer>yes</answer>", props);
        Assert.Equal("yes", result["answer"]);
    }

    [Fact]
    public void SingleTag_FirstMatchWins()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "answer",
        };
        Dictionary<string, string> result = OutputFormatExtractor.Extract(
            "<answer>1</answer><answer>2</answer>",
            props);
        Assert.Equal("1", result["answer"]);
    }

    [Fact]
    public void MultiTag_ReturnsOnePerTag()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.MultiTag,
            OutputFormatTagNames = "thinking,answer",
        };
        Dictionary<string, string> result = OutputFormatExtractor.Extract(
            "<answer>final</answer> blah <thinking>steps</thinking>",
            props);
        Assert.Equal("steps", result["thinking"]);
        Assert.Equal("final", result["answer"]);
    }

    [Fact]
    public void Json_ReturnsRawCleanedJsonUnderJsonKey()
    {
        AiQueryProperties props = new() { OutputFormatType = OutputFormatType.Json };
        Dictionary<string, string> result = OutputFormatExtractor.Extract("{\"a\":1}", props);
        Assert.Equal("{\"a\":1}", result["Json"]);
    }

    [Fact]
    public void JsonInTag_ReturnsCleanedInnerJsonKeyedByTagName()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
        };
        Dictionary<string, string> result = OutputFormatExtractor.Extract(
            "<answer>```json\n{\"a\":1}\n```</answer>",
            props);
        Assert.Equal("{\"a\":1}", result["answer"]);
    }

    [Fact]
    public void GetJsonForBlockResponse_Json_ReturnsCleaned()
    {
        AiQueryProperties props = new() { OutputFormatType = OutputFormatType.Json };
        string? json = OutputFormatExtractor.GetJsonForBlockResponse("{\"a\":1}", props);
        Assert.Equal("{\"a\":1}", json);
    }

    [Fact]
    public void GetJsonForBlockResponse_JsonInTag_ReturnsInner()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
        };
        string? json = OutputFormatExtractor.GetJsonForBlockResponse(
            "<answer>{\"a\":1}</answer>",
            props);
        Assert.Equal("{\"a\":1}", json);
    }

    [Fact]
    public void GetJsonForBlockResponse_TagOnly_ReturnsNull()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "answer",
        };
        string? json = OutputFormatExtractor.GetJsonForBlockResponse("<answer>x</answer>", props);
        Assert.Null(json);
    }

    [Fact]
    public void MultiTag_MissingTagOmittedFromResult()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.MultiTag,
            OutputFormatTagNames = "thinking,answer",
        };
        Dictionary<string, string> result = OutputFormatExtractor.Extract("<answer>x</answer>", props);
        Assert.False(result.ContainsKey("thinking"));
        Assert.Equal("x", result["answer"]);
    }

    [Fact]
    public void JsonInTag_MissingTag_ReturnsEmpty()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
        };
        Dictionary<string, string> result = OutputFormatExtractor.Extract("{\"a\":1}", props);
        Assert.Empty(result);
    }

    [Fact]
    public void GetJsonForBlockResponse_None_ReturnsNull()
    {
        AiQueryProperties props = new() { OutputFormatType = OutputFormatType.None };
        Assert.Null(OutputFormatExtractor.GetJsonForBlockResponse("anything", props));
    }

    [Fact]
    public void GetJsonForBlockResponse_JsonInTag_InvalidTagName_ReturnsNull()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "1bad",
        };
        Assert.Null(OutputFormatExtractor.GetJsonForBlockResponse("<1bad>{\"a\":1}</1bad>", props));
    }

    [Fact]
    public void GetJsonForBlockResponse_JsonInTag_MissingTag_ReturnsNull()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
        };
        Assert.Null(OutputFormatExtractor.GetJsonForBlockResponse("not the right tag", props));
    }
}
