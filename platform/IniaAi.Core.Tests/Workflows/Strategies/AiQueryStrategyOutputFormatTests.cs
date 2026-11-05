using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// Unit tests for the small helpers that implement the AI Query block's output-format
/// retry / extraction surface. The retry loop end-to-end is exercised via the fixture-
/// based integration tests in <see cref="AiQueryStrategyTests"/>.
/// </summary>
public class AiQueryStrategyOutputFormatTests
{
    [Fact]
    public void MergeExtractedValues_NullExisting_AssignsIncoming()
    {
        AiQueryProperties props = new();
        Dictionary<string, string> incoming = new()
        {
            ["answer"] = "yes",
        };

        AiQueryStrategy.MergeExtractedValues(props, incoming);

        Assert.NotNull(props.ExtractedValues);
        Assert.Equal("yes", props.ExtractedValues["answer"]);
    }

    [Fact]
    public void MergeExtractedValues_LastWriteWinsAcrossChunks()
    {
        AiQueryProperties props = new()
        {
            ExtractedValues = new Dictionary<string, string>
            {
                ["answer"] = "first",
                ["thinking"] = "deep",
            },
        };
        Dictionary<string, string> nextChunk = new()
        {
            ["answer"] = "second",
            ["sources"] = "doc-1",
        };

        AiQueryStrategy.MergeExtractedValues(props, nextChunk);

        Assert.Equal("second", props.ExtractedValues!["answer"]);
        Assert.Equal("deep", props.ExtractedValues["thinking"]);
        Assert.Equal("doc-1", props.ExtractedValues["sources"]);
    }

    [Fact]
    public void StripCiteMarks_RemovesTheMarksAModelWroteIntoItsAnswer()
    {
        ResponseContentSequence response =
        [
            new ResponseCommonContent("Cash is <cite index=\"1-3\">96,410</cite>."),
            new ResponseCommonContent("{ \"cash\": \"<cite index=\\\"1-3\\\">96410</cite>\" }"),
        ];

        AiQueryStrategy.StripCiteMarks(response);

        Assert.Equal(["Cash is 96,410.", "{ \"cash\": \"96410\" }"], response.Select(content => content.TextContent));
    }

    [Fact]
    public void GetJsonResponseForFormat_FormatNone_ReturnsNull()
    {
        AiQueryProperties props = new() { OutputFormatType = OutputFormatType.None };
        Assert.Null(AiQueryStrategy.GetJsonResponseForFormat(props));
    }

    [Fact]
    public void GetJsonResponseForFormat_TagOnly_ReturnsNull()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.SingleTag,
            OutputFormatTagName = "answer",
            ExtractedValues = new Dictionary<string, string> { ["answer"] = "yes" },
        };
        Assert.Null(AiQueryStrategy.GetJsonResponseForFormat(props));
    }

    [Fact]
    public void GetJsonResponseForFormat_Json_ReturnsJsonKey()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
            ExtractedValues = new Dictionary<string, string> { ["Json"] = "{\"a\":1}" },
        };
        Assert.Equal("{\"a\":1}", AiQueryStrategy.GetJsonResponseForFormat(props));
    }

    [Fact]
    public void GetJsonResponseForFormat_JsonInTag_ReturnsTagKeyValue()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = "answer",
            ExtractedValues = new Dictionary<string, string> { ["answer"] = "{\"a\":1}" },
        };
        Assert.Equal("{\"a\":1}", AiQueryStrategy.GetJsonResponseForFormat(props));
    }

    [Fact]
    public void GetJsonResponseForFormat_NoExtractedValues_ReturnsNull()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
        };
        Assert.Null(AiQueryStrategy.GetJsonResponseForFormat(props));
    }

    [Fact]
    public void MergeExtractedValues_EmptyIncoming_LeavesExistingUnchanged()
    {
        AiQueryProperties props = new()
        {
            ExtractedValues = new Dictionary<string, string> { ["answer"] = "x" },
        };
        AiQueryStrategy.MergeExtractedValues(props, []);
        Assert.Single(props.ExtractedValues!);
        Assert.Equal("x", props.ExtractedValues["answer"]);
    }

    [Fact]
    public void MergeExtractedValues_BothEmpty_AssignsEmpty()
    {
        AiQueryProperties props = new();
        AiQueryStrategy.MergeExtractedValues(props, []);
        Assert.NotNull(props.ExtractedValues);
        Assert.Empty(props.ExtractedValues!);
    }

    [Fact]
    public void GetJsonResponseForFormat_MultiTag_ReturnsNull()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.MultiTag,
            ExtractedValues = new Dictionary<string, string> { ["answer"] = "y" },
        };
        Assert.Null(AiQueryStrategy.GetJsonResponseForFormat(props));
    }

    [Fact]
    public void GetJsonResponseForFormat_JsonInTag_NullTagName_ReturnsNull()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.JsonInTag,
            OutputFormatTagName = null,
            ExtractedValues = new Dictionary<string, string> { ["answer"] = "{\"a\":1}" },
        };
        Assert.Null(AiQueryStrategy.GetJsonResponseForFormat(props));
    }

    [Fact]
    public void GetJsonResponseForFormat_Json_KeyAbsent_ReturnsNull()
    {
        AiQueryProperties props = new()
        {
            OutputFormatType = OutputFormatType.Json,
            ExtractedValues = new Dictionary<string, string> { ["other"] = "x" },
        };
        Assert.Null(AiQueryStrategy.GetJsonResponseForFormat(props));
    }
}
