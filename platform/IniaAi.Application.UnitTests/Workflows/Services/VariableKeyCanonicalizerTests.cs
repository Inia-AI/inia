using System.Text.Json;
using IniaAi.Application.Workflows.Services;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Workflows.Services;

/// <summary>
/// Pure-helper unit tests for <see cref="VariableKeyCanonicalizer.CanonicalizeVariableKeys"/>.
/// </summary>
public class VariableKeyCanonicalizerTests
{
    private static JsonElement Str(string value)
    {
        return JsonSerializer.SerializeToElement(value);
    }

    private static Workflow WorkflowWithTags(params string[] tags)
    {
        var workflow = new Workflow { Id = "wf-1" };
        foreach (string tag in tags)
        {
            _ = new WorkflowVariable(workflow, tag) { Type = WorkflowVariableTypes.String };
        }

        return workflow;
    }

    [Fact]
    public void CanonicalizeVariableKeys_WhenNullOrEmpty_ReturnsEmpty()
    {
        Workflow workflow = WorkflowWithTags("InterimPeriod");

        Dictionary<string, JsonElement> result =
            VariableKeyCanonicalizer.CanonicalizeVariableKeys(null, workflow.Variables);

        Assert.Empty(result);
    }

    [Fact]
    public void CanonicalizeVariableKeys_WhenExactCanonicalKey_Unchanged()
    {
        Workflow workflow = WorkflowWithTags("InterimPeriod");
        var supplied = new Dictionary<string, JsonElement> { ["InterimPeriod"] = Str("12") };

        Dictionary<string, JsonElement> result =
            VariableKeyCanonicalizer.CanonicalizeVariableKeys(supplied, workflow.Variables);

        _ = Assert.Single(result);
        Assert.Equal("12", result["InterimPeriod"].GetString());
    }

    [Fact]
    public void CanonicalizeVariableKeys_WhenCamelCaseKey_MapsToCanonicalTag()
    {
        Workflow workflow = WorkflowWithTags("InterimPeriod");
        var supplied = new Dictionary<string, JsonElement> { ["interimPeriod"] = Str("12") };

        Dictionary<string, JsonElement> result =
            VariableKeyCanonicalizer.CanonicalizeVariableKeys(supplied, workflow.Variables);

        _ = Assert.Single(result);
        Assert.True(result.ContainsKey("InterimPeriod"));
        Assert.False(result.ContainsKey("interimPeriod"));
        Assert.Equal("12", result["InterimPeriod"].GetString());
    }

    [Fact]
    public void CanonicalizeVariableKeys_WhenTwinWithExactCase_ExactCaseWins()
    {
        Workflow workflow = WorkflowWithTags("InterimPeriod");
        var supplied = new Dictionary<string, JsonElement>
        {
            ["interimPeriod"] = Str("12"),
            ["InterimPeriod"] = Str("99"),
        };

        Dictionary<string, JsonElement> result =
            VariableKeyCanonicalizer.CanonicalizeVariableKeys(supplied, workflow.Variables);

        _ = Assert.Single(result);
        Assert.Equal("99", result["InterimPeriod"].GetString());
    }

    [Fact]
    public void CanonicalizeVariableKeys_WhenTwinNeitherExact_OrdinalFirstWins()
    {
        // Neither supplied key matches the canonical casing, so the ordinal tiebreak decides.
        // Ordinal ordering puts the all-uppercase 'INTERIMPERIOD' before lowercase 'interimperiod'
        // (uppercase code points sort first), so its value must win deterministically.
        Workflow workflow = WorkflowWithTags("InterimPeriod");
        var supplied = new Dictionary<string, JsonElement>
        {
            ["interimperiod"] = Str("lower"),
            ["INTERIMPERIOD"] = Str("upper"),
        };

        Dictionary<string, JsonElement> result =
            VariableKeyCanonicalizer.CanonicalizeVariableKeys(supplied, workflow.Variables);

        _ = Assert.Single(result);
        Assert.Equal("InterimPeriod", result.Keys.Single());
        Assert.Equal("upper", result["InterimPeriod"].GetString());
    }

    [Fact]
    public void CanonicalizeVariableKeys_WhenUnknownKey_CarriedThroughVerbatim_WithValue()
    {
        Workflow workflow = WorkflowWithTags("InterimPeriod");
        var supplied = new Dictionary<string, JsonElement> { ["someUnknownKey"] = Str("keep-me") };

        Dictionary<string, JsonElement> result =
            VariableKeyCanonicalizer.CanonicalizeVariableKeys(supplied, workflow.Variables);

        _ = Assert.Single(result);
        Assert.True(result.ContainsKey("someUnknownKey"));
        Assert.Equal("keep-me", result["someUnknownKey"].GetString());
    }

    [Fact]
    public void CanonicalizeVariableKeys_WhenTwinDiscardsValue_LogsExactlyOneWarningNamingDiscardedKey()
    {
        Workflow workflow = WorkflowWithTags("InterimPeriod");
        ILogger logger = Substitute.For<ILogger>();
        _ = logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var supplied = new Dictionary<string, JsonElement>
        {
            ["interimPeriod"] = Str("12"),
            ["InterimPeriod"] = Str("99"),
        };

        _ = VariableKeyCanonicalizer.CanonicalizeVariableKeys(supplied, workflow.Variables, logger);

        // Exactly one Warning, and its formatted message names the discarded key.
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains("interimPeriod")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
