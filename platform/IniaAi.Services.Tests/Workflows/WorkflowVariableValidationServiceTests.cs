using ErrorOr;
using IniaAi.Application.Workflows.Interfaces;
using IniaAi.Application.Workflows.Services;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Text.Json;

namespace IniaAi.Services.Tests.Workflows;

public class WorkflowVariableValidationServiceTests
{
    private readonly IWorkflowVariableValidationService _sut;

    public WorkflowVariableValidationServiceTests()
    {
        _sut = new WorkflowVariableValidationService();
    }

    [Fact]
    public void ValidateVariables_WhenRequiredVariableMissing_ReturnsError()
    {
        // Arrange
        var workflow = new Workflow();

        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "testVar",
            DisplayName = "Test Variable",
            IsRequired = true
        };

        var variables = new Dictionary<string, JsonElement>();

        // Act
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert
        _ = Assert.Single(result);
        Assert.Equal("WorkflowVariable.Required", result[0].Code);
        Assert.Contains("Test Variable", result[0].Description);
        Assert.Contains("testVar", result[0].Description);
    }

    [Fact]
    public void ValidateVariables_WhenOptionalVariableMissing_ReturnsNoError()
    {
        // Arrange
        var workflow = new Workflow();
        var variable = new WorkflowVariable(workflow)
        {
            ReplacementTag = "testVar",
            DisplayName = "Test Variable",
            IsRequired = false
        };
        workflow.Variables.Add(variable);

        var variables = new Dictionary<string, JsonElement>();

        // Act
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(WorkflowVariableTypes.String, JsonValueKind.String, true)]
    [InlineData(WorkflowVariableTypes.String, JsonValueKind.Number, false)]
    [InlineData(WorkflowVariableTypes.Number, JsonValueKind.Number, true)]
    [InlineData(WorkflowVariableTypes.Number, JsonValueKind.String, false)]
    [InlineData(WorkflowVariableTypes.DateTime, JsonValueKind.String, true)]
    [InlineData(WorkflowVariableTypes.DateTime, JsonValueKind.Number, false)]
    [InlineData(WorkflowVariableTypes.List, JsonValueKind.Array, true)]
    [InlineData(WorkflowVariableTypes.List, JsonValueKind.String, false)]
    public void ValidateVariables_ValidatesCorrectType(WorkflowVariableTypes type, JsonValueKind valueKind, bool shouldBeValid)
    {
        // Arrange
        var workflow = new Workflow();

        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "testVar",
            DisplayName = "Test Variable",
            Type = type
        };

        JsonElement jsonElement = valueKind switch
        {
            JsonValueKind.String => type is WorkflowVariableTypes.DateTime
                ? JsonSerializer.Deserialize<JsonElement>("\"2024-03-20T12:00:00Z\"")
                : JsonSerializer.Deserialize<JsonElement>("\"test\""),
            JsonValueKind.Number => JsonSerializer.Deserialize<JsonElement>("123"),
            JsonValueKind.Array => JsonSerializer.Deserialize<JsonElement>("[\"test\"]"),
            _ => throw new ArgumentException("Unsupported value kind")
        };

        var variables = new Dictionary<string, JsonElement>
        {
            { "testVar", jsonElement }
        };

        // Act
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert
        if (shouldBeValid)
        {
            Assert.Empty(result);
        }
        else
        {
            _ = Assert.Single(result);
            Assert.Equal("WorkflowVariable.InvalidType", result[0].Code);
        }
    }

    [Fact]
    public void ValidateVariables_WhenDropdownValueNotInOptions_ReturnsError()
    {
        // Arrange
        var workflow = new Workflow();

        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "testVar",
            DisplayName = "Test Variable",
            Type = WorkflowVariableTypes.Dropdown,
            Options = new Dictionary<string, string> { { "option1", "Option 1" }, { "option2", "Option 2" } }
        };

        JsonElement jsonElement = JsonSerializer.Deserialize<JsonElement>("\"invalidOption\"");
        var variables = new Dictionary<string, JsonElement>
        {
            { "testVar", jsonElement }
        };

        // Act
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert
        _ = Assert.Single(result);
        Assert.Equal("WorkflowVariable.InvalidOption", result[0].Code);
        Assert.Contains("invalidOption", result[0].Description);
    }

    [Fact]
    public void ValidateVariables_WhenMultiSelectValueNotInOptions_ReturnsError()
    {
        // Arrange
        var workflow = new Workflow();

        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "testVar",
            DisplayName = "Test Variable",
            Type = WorkflowVariableTypes.MultiSelect,
            Options = new Dictionary<string, string> { { "option1", "Option 1" }, { "option2", "Option 2" } }
        };

        JsonElement jsonElement = JsonSerializer.Deserialize<JsonElement>("[\"option1\", \"invalidOption\"]");
        var variables = new Dictionary<string, JsonElement>
        {
            { "testVar", jsonElement }
        };

        // Act
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert
        _ = Assert.Single(result);
        Assert.Equal("WorkflowVariable.InvalidOption", result[0].Code);
        Assert.Contains("invalidOption", result[0].Description);
    }

    [Theory]
    [InlineData("2026-09-25")]
    [InlineData("2026-09-25T17:45")]
    [InlineData("2026-09-25T17:45:00Z")]
    [InlineData("2026-09-25T17:45:00+02:00")]
    [InlineData(" 2026-09-25 ")]
    public void ValidateVariables_DateFromAClientOrTheApi_IsAccepted(string value)
    {
        var workflow = new Workflow();
        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "asOfDate",
            DisplayName = "As of date",
            Type = WorkflowVariableTypes.DateTime,
        };
        var variables = new Dictionary<string, JsonElement>
        {
            { "asOfDate", JsonSerializer.SerializeToElement(value) },
        };

        List<Error> result = _sut.ValidateVariables(workflow, variables);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateVariables_WhenDateTimeInvalid_ReturnsError()
    {
        // Arrange
        var workflow = new Workflow();

        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "testVar",
            DisplayName = "Test Variable",
            Type = WorkflowVariableTypes.DateTime
        };

        JsonElement jsonElement = JsonSerializer.Deserialize<JsonElement>("\"invalid-date\"");
        var variables = new Dictionary<string, JsonElement>
        {
            { "testVar", jsonElement }
        };

        // Act
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert
        _ = Assert.Single(result);
        Assert.Equal("WorkflowVariable.InvalidDateTime", result[0].Code);
        Assert.Contains("invalid-date", result[0].Description);
    }

    [Fact]
    public void ValidateVariables_WhenRequiredListEmpty_ReturnsError()
    {
        // Arrange
        var workflow = new Workflow();

        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "testVar",
            DisplayName = "Test Variable",
            Type = WorkflowVariableTypes.List
        };

        JsonElement jsonElement = JsonSerializer.Deserialize<JsonElement>("[]");
        var variables = new Dictionary<string, JsonElement>
        {
            { "testVar", jsonElement }
        };

        // Act
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert
        _ = Assert.Single(result);
        Assert.Equal("WorkflowVariable.InvalidList", result[0].Code);
    }

    [Fact]
    public void ValidateVariables_WhenNonRequiredListEmpty_ReturnsNoError()
    {
        // Arrange
        var workflow = new Workflow();

        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "testVar",
            DisplayName = "Test Variable",
            Type = WorkflowVariableTypes.List,
            IsRequired = false
        };

        JsonElement jsonElement = JsonSerializer.Deserialize<JsonElement>("[]");
        var variables = new Dictionary<string, JsonElement>
        {
            { "testVar", jsonElement }
        };

        // Act
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(JsonValueKind.Array, "[\"item1\",\"item2\"]", "item1|item2")]
    [InlineData(JsonValueKind.String, "\"test\"", "test")]
    [InlineData(JsonValueKind.Number, "123", "123")]
    [InlineData(JsonValueKind.True, "true", "true")]
    [InlineData(JsonValueKind.False, "false", "false")]
    public void ConvertVariablesToStrings_ConvertsCorrectly(JsonValueKind _, string json, string expected)
    {
        // Arrange
        JsonElement jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var variables = new Dictionary<string, JsonElement>
        {
            { "testVar", jsonElement }
        };

        // Act
        Dictionary<string, string> result = _sut.ConvertVariablesToStrings(variables);

        // Assert
        KeyValuePair<string, string> singleResult = Assert.Single(result);
        Assert.Equal(expected, singleResult.Value);
    }

    [Fact]
    public void ConvertVariablesToStrings_WhenVariablesNull_ReturnsEmptyDictionary()
    {
        // Act
        Dictionary<string, string> result = _sut.ConvertVariablesToStrings(null);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateVariables_WhenTwinCasingKeysSupplied_DoesNotThrow()
    {
        // Arrange — both a PascalCase and camelCase twin for the same tag reach the validator.
        // The OrdinalIgnoreCase normalization must fold them, not throw ArgumentException (the 500 bug).
        var workflow = new Workflow();
        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "FundNumber",
            DisplayName = "Fund Number",
            IsRequired = false,
            Type = WorkflowVariableTypes.String,
        };

        var variables = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["FundNumber"] = JsonSerializer.SerializeToElement("1"),
            ["fundNumber"] = JsonSerializer.SerializeToElement("1"),
        };

        // Act — must not throw.
        List<Error> result = _sut.ValidateVariables(workflow, variables);

        // Assert — folds to a single valid string variable, no errors.
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateVariables_NamesTheFailingVariableTagInEachErrorsMetadata()
    {
        var workflow = new Workflow();
        _ = new WorkflowVariable(workflow, "account_holder_type")
        {
            DisplayName = "Account Holder",
            Type = WorkflowVariableTypes.Dropdown,
            IsRequired = true,
            Options = new Dictionary<string, string> { ["Individual"] = "Individual", ["Company"] = "Company" },
        };
        _ = new WorkflowVariable(workflow, "as_of")
        {
            DisplayName = "As of",
            Type = WorkflowVariableTypes.DateTime,
            IsRequired = false,
        };

        List<Error> result = _sut.ValidateVariables(workflow, new Dictionary<string, JsonElement>
        {
            ["as_of"] = JsonSerializer.SerializeToElement("not a date"),
        });

        Assert.Equal(
            ["account_holder_type", "as_of"],
            result.Select(error => error.Metadata?[WorkflowVariableValidationService.REPLACEMENT_TAG_METADATA_KEY]).ToList());
    }
}
