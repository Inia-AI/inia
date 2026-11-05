using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class MergeJsonPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_FromEmptySource_CreatesEmptyList()
    {
        var props = new MergeJsonProperties();

        var state = new MergeJsonPropertiesExecutionState(props);

        Assert.NotNull(state);
        Assert.Empty(state.JsonInputs);
    }

    [Fact]
    public void Constructor_CopiesJsonInputsAsDefensiveCopy()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}", "{\"a\":1}" },
        };

        var state = new MergeJsonPropertiesExecutionState(props);

        Assert.Equal("{block_0}", state.JsonInputs[0]);
        Assert.Equal("{\"a\":1}", state.JsonInputs[1]);

        // Mutate source — state should be unaffected
        props.JsonInputs[0] = "modified";
        Assert.Equal("{block_0}", state.JsonInputs[0]);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllItems()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}", "static-value" },
        };
        var state = new MergeJsonPropertiesExecutionState(props);

        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { ["block_0"] = "\"hello\"" });

        Assert.Equal("\"hello\"", state.JsonInputs[0]);
        Assert.Equal("static-value", state.JsonInputs[1]);
    }

    [Fact]
    public void ReplaceAllPlaceholders_WholePlaceholderItem_SubstitutesJsonDocumentVerbatim()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0.json}" },
        };
        var state = new MergeJsonPropertiesExecutionState(props);

        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?>
        {
            ["block_0.json"] = """{"a": "line1\nline2"}""",
        });

        Assert.Equal("""{"a": "line1\nline2"}""", state.JsonInputs[0]);
    }

    [Fact]
    public void ReplaceAllPlaceholders_EmbeddedPlaceholder_JsonEscapesSubstitutedValue()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { """{"note": "{comment}"}""" },
        };
        var state = new MergeJsonPropertiesExecutionState(props);

        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?>
        {
            ["comment"] = "say \"hi\"\nbye",
        });

        Assert.Equal("""{"note": "say \"hi\"\nbye"}""", state.JsonInputs[0]);
    }

    [Fact]
    public void ReplaceAllPlaceholders_UpstreamJsonNestedInBareValuePosition_SubstitutesVerbatim()
    {
        // The live "{"Peer1": {peer.analysis}}" pattern: a whole upstream JSON object nested as a
        // value must pass through un-escaped, or MergeJson's per-input JToken.Parse drops it.
        var props = new MergeJsonProperties
        {
            JsonInputs = { """{"Peer1": {peer.analysis}}""" },
        };
        var state = new MergeJsonPropertiesExecutionState(props);

        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?>
        {
            ["peer.analysis"] = """{"rating": "A", "summary": "line1\nline2"}""",
        });

        Assert.Equal("""{"Peer1": {"rating": "A", "summary": "line1\nline2"}}""", state.JsonInputs[0]);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}" },
        };

        props.SetExecutionState(true, new Dictionary<string, string?> { ["block_0"] = "\"hello\"" });

        Assert.NotNull(props.ExecutionState);
        Assert.Equal("\"hello\"", props.ExecutionState.JsonInputs[0]);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}" },
        };
        Assert.Null(props.ExecutionState);

        props.UpdateExecutionState(true, new Dictionary<string, string?> { ["block_0"] = "\"hello\"" });

        Assert.NotNull(props.ExecutionState);
        Assert.Equal("\"hello\"", props.ExecutionState.JsonInputs[0]);
    }

    [Fact]
    public void ClearExecutionState_WhenSet_ClearsIt()
    {
        var props = new MergeJsonProperties();
        props.SetExecutionState(true, []);
        Assert.NotNull(props.ExecutionState);

        props.ClearExecutionState();

        Assert.Null(props.ExecutionState);
    }

    [Fact]
    public void Constructor_DeepCopiesInputExtractions()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}" },
            InputExtractions =
            {
                new MergeJsonInputExtraction
                {
                    Mode = IniaAi.Domain.Entities.Workflows.Enums.JsonExtractionMode.XmlTagPattern,
                    Pattern = "page*",
                },
            },
        };

        var state = new MergeJsonPropertiesExecutionState(props);

        Assert.Single(state.InputExtractions);
        Assert.Equal(IniaAi.Domain.Entities.Workflows.Enums.JsonExtractionMode.XmlTagPattern, state.InputExtractions[0].Mode);
        Assert.Equal("page*", state.InputExtractions[0].Pattern);

        // Mutating the state must not affect source — it's a deep copy.
        state.InputExtractions[0].Pattern = "changed";
        Assert.Equal("page*", props.InputExtractions[0].Pattern);
    }
}
