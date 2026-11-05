using System.Text.Json;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetWorkflowExecution;

public class WorkflowExecutionVariableResolverTests
{
    private const string MASK = "••••";

    private static JsonElement Element(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static WorkflowVariable Define(
        Workflow workflow,
        string tag,
        WorkflowVariableTypes type,
        string? displayName = null,
        string? description = null,
        Dictionary<string, string>? options = null,
        bool isRequired = false,
        bool isHidden = false,
        bool isConfidential = false)
    {
        return new WorkflowVariable(workflow, tag)
        {
            DisplayName = displayName ?? tag,
            Description = description ?? string.Empty,
            Type = type,
            IsRequired = isRequired,
            IsHidden = isHidden,
            IsConfidential = isConfidential,
            Options = options ?? [],
        };
    }

    [Fact]
    public void Resolve_String_ReturnsRawString()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "name", WorkflowVariableTypes.String);
        var raw = new Dictionary<string, JsonElement> { ["name"] = Element("\"Acme Corp\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto entry = Assert.Single(result);
        Assert.Equal("Acme Corp", entry.DisplayValue);
        Assert.Equal("String", entry.Type);
    }

    [Fact]
    public void Resolve_Number_Integer_FormatsAsInteger()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "count", WorkflowVariableTypes.Number);
        var raw = new Dictionary<string, JsonElement> { ["count"] = Element("42") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("42", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_Number_Double_FormatsAsDouble()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "ratio", WorkflowVariableTypes.Number);
        var raw = new Dictionary<string, JsonElement> { ["ratio"] = Element("3.14") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("3.14", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_DateTime_PassthroughIsoString()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "due", WorkflowVariableTypes.DateTime);
        var raw = new Dictionary<string, JsonElement>
        {
            ["due"] = Element("\"2026-05-27T10:00:00Z\""),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("2026-05-27T10:00:00Z", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_List_JoinsValuesWithCommaSpace()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "topics", WorkflowVariableTypes.List);
        var raw = new Dictionary<string, JsonElement>
        {
            ["topics"] = Element("[\"risk\", \"strategy\", \"esg\"]"),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("risk, strategy, esg", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_Dropdown_KnownKey_MapsToDisplayName()
    {
        var workflow = new Workflow();
        _ = Define(
            workflow,
            "industry",
            WorkflowVariableTypes.Dropdown,
            options: new Dictionary<string, string>
            {
                ["RE"] = "Real Estate",
                ["FIN"] = "Financials",
            });
        var raw = new Dictionary<string, JsonElement> { ["industry"] = Element("\"RE\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("Real Estate", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_Dropdown_UnknownKey_FallsBackToRawKey()
    {
        var workflow = new Workflow();
        _ = Define(
            workflow,
            "industry",
            WorkflowVariableTypes.Dropdown,
            options: new Dictionary<string, string> { ["RE"] = "Real Estate" });
        var raw = new Dictionary<string, JsonElement> { ["industry"] = Element("\"UNKNOWN\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("UNKNOWN", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_Dropdown_CaseSensitiveLookup()
    {
        var workflow = new Workflow();
        _ = Define(
            workflow,
            "industry",
            WorkflowVariableTypes.Dropdown,
            options: new Dictionary<string, string> { ["RE"] = "Real Estate" });
        var raw = new Dictionary<string, JsonElement> { ["industry"] = Element("\"re\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("re", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_MultiSelect_JoinsOptionDisplayNames()
    {
        var workflow = new Workflow();
        _ = Define(
            workflow,
            "tags",
            WorkflowVariableTypes.MultiSelect,
            options: new Dictionary<string, string>
            {
                ["a"] = "Alpha",
                ["b"] = "Beta",
                ["c"] = "Gamma",
            });
        var raw = new Dictionary<string, JsonElement>
        {
            ["tags"] = Element("[\"a\", \"c\"]"),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("Alpha, Gamma", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_MultiSelect_UnknownKeyFallsBackToRaw()
    {
        var workflow = new Workflow();
        _ = Define(
            workflow,
            "tags",
            WorkflowVariableTypes.MultiSelect,
            options: new Dictionary<string, string> { ["a"] = "Alpha" });
        var raw = new Dictionary<string, JsonElement>
        {
            ["tags"] = Element("[\"a\", \"missing\"]"),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("Alpha, missing", Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_Confidential_WithValue_MasksDisplayValue()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "apiKey", WorkflowVariableTypes.String, isConfidential: true);
        var raw = new Dictionary<string, JsonElement> { ["apiKey"] = Element("\"secret-token\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto entry = Assert.Single(result);
        Assert.Equal(MASK, entry.DisplayValue);
        Assert.True(entry.IsConfidential);
    }

    [Fact]
    public void Resolve_Confidential_WithoutValue_LeavesDisplayValueNull()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "apiKey", WorkflowVariableTypes.String, isConfidential: true);
        var raw = new Dictionary<string, JsonElement>();

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto entry = Assert.Single(result);
        Assert.Null(entry.DisplayValue);
        Assert.True(entry.IsConfidential);
    }

    [Fact]
    public void Resolve_Hidden_FlagPreserved()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "internal", WorkflowVariableTypes.String, isHidden: true);
        var raw = new Dictionary<string, JsonElement> { ["internal"] = Element("\"x\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto entry = Assert.Single(result);
        Assert.True(entry.IsHidden);
        Assert.Equal("x", entry.DisplayValue);
    }

    [Fact]
    public void Resolve_MissingRawValue_DisplayValueIsNull()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "name", WorkflowVariableTypes.String, isRequired: true);
        var raw = new Dictionary<string, JsonElement>();

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto entry = Assert.Single(result);
        Assert.Null(entry.DisplayValue);
        Assert.True(entry.IsRequired);
    }

    [Fact]
    public void Resolve_NullJsonValue_DisplayValueIsNull()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "name", WorkflowVariableTypes.String);
        var raw = new Dictionary<string, JsonElement> { ["name"] = Element("null") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Null(Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_EmptyStringValue_DisplayValueIsNull()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "name", WorkflowVariableTypes.String);
        var raw = new Dictionary<string, JsonElement> { ["name"] = Element("\"\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Null(Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_PreservesDefinitionOrder()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "first", WorkflowVariableTypes.String);
        _ = Define(workflow, "second", WorkflowVariableTypes.String);
        _ = Define(workflow, "third", WorkflowVariableTypes.String);
        var raw = new Dictionary<string, JsonElement>
        {
            ["third"] = Element("\"c\""),
            ["first"] = Element("\"a\""),
            ["second"] = Element("\"b\""),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal(new[] { "first", "second", "third" }, result.Select(r => r.ReplacementTag));
    }

    [Fact]
    public void Resolve_TagCasingDiffersFromRawKey_MergesIntoSingleEntry()
    {
        var workflow = new Workflow();
        _ = Define(
            workflow,
            "CorporationAssessmentYear",
            WorkflowVariableTypes.String,
            displayName: "Assessment Year");
        var raw = new Dictionary<string, JsonElement>
        {
            ["corporationAssessmentYear"] = Element("\"2025\""),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto entry = Assert.Single(result);
        Assert.Equal("Assessment Year", entry.DisplayName);
        Assert.Equal("2025", entry.DisplayValue);
    }

    [Fact]
    public void Resolve_RawKeysCollidingByCaseOnly_OverwriteWithoutThrowing()
    {
        var workflow = new Workflow();
        _ = Define(
            workflow,
            "assessmentYear",
            WorkflowVariableTypes.String,
            displayName: "Assessment Year");
        var raw = new Dictionary<string, JsonElement>
        {
            ["AssessmentYear"] = Element("\"2024\""),
            ["assessmentYear"] = Element("\"2025\""),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto entry = Assert.Single(result);
        Assert.Equal("Assessment Year", entry.DisplayName);
        Assert.Contains(entry.DisplayValue, new[] { "2024", "2025" });
    }

    [Fact]
    public void Resolve_OrphanRawKey_AppendedAsStringWithRawLabel()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "defined", WorkflowVariableTypes.String);
        var raw = new Dictionary<string, JsonElement>
        {
            ["defined"] = Element("\"value\""),
            ["legacyKey"] = Element("\"legacy value\""),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal(2, result.Count);
        WorkflowExecutionVariableValueDto orphan = result[1];
        Assert.Equal("legacyKey", orphan.ReplacementTag);
        Assert.Equal("legacyKey", orphan.DisplayName);
        Assert.Equal("String", orphan.Type);
        Assert.Equal("legacy value", orphan.DisplayValue);
        Assert.False(orphan.IsConfidential);
        Assert.False(orphan.IsHidden);
        Assert.False(orphan.IsRequired);
    }

    [Fact]
    public void Resolve_OrphanRawKey_NumberValue_StringifiesValueAndLabelsAsNumber()
    {
        var workflow = new Workflow();
        var raw = new Dictionary<string, JsonElement> { ["legacyCount"] = Element("17") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto orphan = Assert.Single(result);
        Assert.Equal("17", orphan.DisplayValue);
        Assert.Equal("Number", orphan.Type);
    }

    [Fact]
    public void Resolve_OrphanRawKey_ArrayValue_LabelsAsList()
    {
        var workflow = new Workflow();
        var raw = new Dictionary<string, JsonElement>
        {
            ["legacyArr"] = Element("[\"a\", \"b\"]"),
        };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto orphan = Assert.Single(result);
        Assert.Equal("a, b", orphan.DisplayValue);
        Assert.Equal("List", orphan.Type);
    }

    [Fact]
    public void Resolve_Confidential_EmptyArray_LeavesDisplayValueNull()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "tags", WorkflowVariableTypes.MultiSelect, isConfidential: true);
        var raw = new Dictionary<string, JsonElement> { ["tags"] = Element("[]") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        WorkflowExecutionVariableValueDto entry = Assert.Single(result);
        Assert.Null(entry.DisplayValue);
        Assert.True(entry.IsConfidential);
    }

    [Fact]
    public void Resolve_EmptyArray_DisplayValueIsNull()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "tags", WorkflowVariableTypes.MultiSelect);
        var raw = new Dictionary<string, JsonElement> { ["tags"] = Element("[]") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Null(Assert.Single(result).DisplayValue);
    }

    [Fact]
    public void Resolve_NoDefinitions_NoRaw_ReturnsEmpty()
    {
        var workflow = new Workflow();

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, null);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_NullRaw_ReturnsDefinitionsWithNullValues()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "name", WorkflowVariableTypes.String);
        _ = Define(workflow, "count", WorkflowVariableTypes.Number);

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, null);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Null(r.DisplayValue));
    }

    [Fact]
    public void Resolve_DescriptionPropagated()
    {
        var workflow = new Workflow();
        _ = Define(
            workflow,
            "name",
            WorkflowVariableTypes.String,
            description: "Helpful description");
        var raw = new Dictionary<string, JsonElement> { ["name"] = Element("\"x\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("Helpful description", Assert.Single(result).Description);
    }

    [Fact]
    public void Resolve_DisplayNamePropagated()
    {
        var workflow = new Workflow();
        _ = Define(workflow, "tag1", WorkflowVariableTypes.String, displayName: "Pretty Name");
        var raw = new Dictionary<string, JsonElement> { ["tag1"] = Element("\"x\"") };

        List<WorkflowExecutionVariableValueDto> result =
            WorkflowExecutionVariableResolver.Resolve(workflow.Variables, raw);

        Assert.Equal("Pretty Name", Assert.Single(result).DisplayName);
    }
}
