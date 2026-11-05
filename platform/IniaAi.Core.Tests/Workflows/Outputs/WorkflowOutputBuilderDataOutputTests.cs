using ErrorOr;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Core.Tests.Workflows.Outputs;

public class WorkflowOutputBuilderDataOutputTests
{
    private const string FACILITIES_JSON = """
        {
          "currency": "NIS",
          "facilities": [
            { "bank": "Example Bank", "limit": 250000, "note": "Overdraft of 250,000 renewed; 17 cheques returned" }
          ],
          "account/movement": { "total_credits": 318204.4 }
        }
        """;

    private readonly WorkflowOutputBuilder _builder = new();
    private readonly Workflow _workflow = new();
    private readonly Section _section;

    public WorkflowOutputBuilderDataOutputTests()
    {
        _section = new Section("Extract", parent: _workflow);
    }

    [Fact]
    public void JsonOutput_IsKeptAsTheBlockProducedIt_WithoutFactMarkers()
    {
        Block block = MergeJsonBlock("facilities_json", "facilities", FACILITIES_JSON);

        ErrorOr<ReportDataOutput> build = _builder.BuildDataOutput(block, Context(), Annotations(Fact("250,000"), Fact("318,204.40")));

        Assert.False(build.IsError);
        Assert.Equal(FACILITIES_JSON, build.Value.Json);
        Assert.Equal("facilities", build.Value.Name);
        Assert.Equal(block.Id, build.Value.BlockId);
        Assert.Equal("report-1", build.Value.WorkflowReportId);
    }

    [Fact]
    public void CitedFigures_PointAtTheValuesThatHoldThem()
    {
        Block block = MergeJsonBlock("facilities_json", "facilities", FACILITIES_JSON);

        ErrorOr<ReportDataOutput> build = _builder.BuildDataOutput(
            block,
            Context(),
            Annotations(Fact("250,000"), Fact("17"), Fact("318,204.40")));

        List<(string Path, string Value)> cited = [.. build.Value.Citations.Select(c => (c.Path, c.Value))];

        Assert.Contains(("/facilities/0/limit", "250000"), cited);
        Assert.Contains(("/facilities/0/note", "250,000"), cited);
        Assert.Contains(("/facilities/0/note", "17"), cited);
        Assert.Contains(("/account~1movement/total_credits", "318204.4"), cited);
        Assert.DoesNotContain(cited, c => c.Path == "/currency" || c.Path == "/facilities/0/bank");
        Assert.All(build.Value.Citations, citation =>
        {
            DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(citation.Sources));
            Assert.Equal("Bank statement.pdf", source.DocumentName);
        });
    }

    [Fact]
    public void OneFigureOnTwoPaths_SharesItsCitationId()
    {
        Block block = MergeJsonBlock("facilities_json", "facilities", FACILITIES_JSON);

        ErrorOr<ReportDataOutput> build = _builder.BuildDataOutput(block, Context(), Annotations(Fact("250,000")));

        DataOutputCitation onLimit = Assert.Single(build.Value.Citations, c => c.Path == "/facilities/0/limit");
        DataOutputCitation onNote = Assert.Single(build.Value.Citations, c => c.Path == "/facilities/0/note");
        Assert.Equal(onLimit.Id, onNote.Id);
    }

    [Fact]
    public void OutputThatIsNotJson_IsRefused_NamingTheBlockAndTheOutput()
    {
        Block block = MergeJsonBlock("facilities_json", "facilities", "Facilities: overdraft of 250,000.");

        ErrorOr<ReportDataOutput> build = _builder.BuildDataOutput(block, Context(), null);

        Assert.True(build.IsError);
        Assert.Equal(
            "The block \"Facilities JSON\" sends its result to other systems as \"facilities\", but its result is not JSON. Make the block return JSON, then run it again.",
            build.FirstError.Description);
    }

    [Fact]
    public void BlockWithNoOutput_IsRefused()
    {
        Block block = MergeJsonBlock("facilities_json", "facilities", null);

        ErrorOr<ReportDataOutput> build = _builder.BuildDataOutput(block, Context(), null);

        Assert.True(build.IsError);
        Assert.Contains("but it produced no result. Check what the block reads", build.FirstError.Description);
    }

    [Fact]
    public void AiQueryWithoutAJsonOutputFormat_IsRefused()
    {
        Block block = new(BlockTypes.AiQuery, _section, "facilities_json")
        {
            Name = "Facilities JSON",
            DataOutputName = "facilities",
            Properties = new AiQueryProperties { OutputFormatType = OutputFormatType.None },
        };
        _ = new BlockResponse(block) { RawResponse = FACILITIES_JSON, JsonResponse = FACILITIES_JSON };

        ErrorOr<ReportDataOutput> build = _builder.BuildDataOutput(block, Context(), null);

        Assert.True(build.IsError);
        Assert.Contains("Set Output format validation to JSON or JSON in Tag", build.FirstError.Description);
    }

    [Fact]
    public void AiQueryWithJsonInTag_SendsTheJsonInsideTheTag()
    {
        const string inner = """{ "limit": 250000 }""";
        Block block = new(BlockTypes.AiQuery, _section, "facilities_json")
        {
            Name = "Facilities JSON",
            DataOutputName = "facilities",
            Properties = new AiQueryProperties
            {
                OutputFormatType = OutputFormatType.JsonInTag,
                OutputFormatTagName = "answer",
            },
        };
        _ = new BlockResponse(block) { RawResponse = $"Reasoning first.\n<answer>{inner}</answer>", JsonResponse = inner };

        ErrorOr<ReportDataOutput> build = _builder.BuildDataOutput(block, Context(), Annotations(Fact("250,000")));

        Assert.Equal(inner, build.Value.Json);
        Assert.Equal("/limit", Assert.Single(build.Value.Citations).Path);
    }

    private Block MergeJsonBlock(string tag, string dataOutputName, string? json)
    {
        Block block = new(BlockTypes.MergeJson, _section, tag)
        {
            Name = "Facilities JSON",
            DataOutputName = dataOutputName,
            Properties = new MergeJsonProperties(),
        };
        _ = new BlockResponse(block) { RawResponse = json, JsonResponse = json };
        return block;
    }

    private static WorkflowOutputContext Context()
    {
        return new WorkflowOutputContext { RootElementId = "report-1" };
    }

    private static BlockAnnotations Annotations(params Fact[] facts)
    {
        var annotations = new BlockAnnotations();
        foreach (Fact fact in facts)
        {
            annotations.AddFact(fact);
        }

        return annotations;
    }

    private static Fact Fact(string value)
    {
        var fact = new Fact(value) { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = $"Statement line: {value}",
            DocumentId = "doc-1",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "Bank statement.pdf",
        });
        return fact;
    }
}
