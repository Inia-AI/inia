using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class AiQueryPropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInUserPrompt_ReturnsTrue()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Hello {user}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{user}"));
    }

    [Fact]
    public void HasPlaceholder_ContainsInSystemPrompt_ReturnsTrue()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            SystemPromptTemplate = "System {system}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{system}"));
    }

    [Fact]
    public void HasPlaceholder_ContainsInTextToCache_ReturnsTrue()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            TextToCache = "Cache {data}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{data}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Hello world",
            SystemPromptTemplate = "System prompt",
            TextToCache = "Some text",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new AiQueryProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Hello {user}, welcome to {place}!",
            SystemPromptTemplate = "System {system}",
            TextToCache = "Cache {data}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("user", result);
        Assert.Contains("place", result);
        Assert.Contains("system", result);
        Assert.Contains("data", result);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Hello world",
            SystemPromptTemplate = "System",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNullProperties()
    {
        // Arrange
        var props = new AiQueryProperties();

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInAllTemplateProperties()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Original User Prompt",
            SystemPromptTemplate = "Original System Prompt",
            TextToCache = "Original Cache Text",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced User Prompt", props.UserPromptTemplate);
        Assert.Equal("Replaced System Prompt", props.SystemPromptTemplate);
        Assert.Equal("Replaced Cache Text", props.TextToCache);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new AiQueryProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void SetExecutionState_CapturesResolvedSignatureFieldValues_ForRealExecution()
    {
        // Arrange — the block's templates reference two workflow-level signature
        // fields; the execution injector stamped their names as candidates and
        // pre-populated inputs with the resolved values.
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Rules: {COGS_instructions} and {REV_rules}",
            CandidateSignatureFieldNames = ["COGS_instructions", "REV_rules"],
        };

        Dictionary<string, string?> inputs = new()
        {
            ["COGS_instructions"] = "client cogs override",
            ["REV_rules"] = "client rev override",
            ["unrelated_block_output"] = "ignore me",
        };

        // Act
        props.SetExecutionState(asPlaceHolder: false, inputs);

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.NotNull(props.ExecutionState.ResolvedSignatureFieldValues);
        Assert.Equal(2, props.ExecutionState.ResolvedSignatureFieldValues.Count);
        Assert.Equal("client cogs override", props.ExecutionState.ResolvedSignatureFieldValues["COGS_instructions"]);
        Assert.Equal("client rev override", props.ExecutionState.ResolvedSignatureFieldValues["REV_rules"]);
        Assert.False(props.ExecutionState.ResolvedSignatureFieldValues.ContainsKey("unrelated_block_output"));
    }

    [Fact]
    public void SetExecutionState_CapturesResolvedSignatureFieldValues_ForPlaceholderPass()
    {
        // The `asPlaceHolder` flag controls how StringExtensions.ReplacePlaceholders
        // wraps the key during substitution (both branches still substitute the value);
        // the actual signature-bearing execution path through WorkflowHandler calls
        // SetExecutionState with asPlaceHolder=true, so capture must still run.
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Rules: {COGS_instructions}",
            CandidateSignatureFieldNames = ["COGS_instructions"],
        };

        Dictionary<string, string?> inputs = new()
        {
            ["COGS_instructions"] = "client cogs override",
        };

        props.SetExecutionState(asPlaceHolder: true, inputs);

        Assert.NotNull(props.ExecutionState);
        Assert.NotNull(props.ExecutionState.ResolvedSignatureFieldValues);
        Assert.Equal("client cogs override", props.ExecutionState.ResolvedSignatureFieldValues["COGS_instructions"]);
    }

    [Fact]
    public void SetExecutionState_SkipsCapture_WhenNoCandidateNamesStamped()
    {
        // Arrange — the injector never stamped CandidateSignatureFieldNames (no
        // workflow-level fields referenced), so nothing must be captured.
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "No signatures here",
        };

        Dictionary<string, string?> inputs = new()
        {
            ["anything"] = "value",
        };

        // Act
        props.SetExecutionState(asPlaceHolder: false, inputs);

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Null(props.ExecutionState.ResolvedSignatureFieldValues);
    }

    [Fact]
    public void SetExecutionState_LegacyInlineDictAlone_DoesNotDriveCapture()
    {
        // The inline SignatureFields dict is legacy input / block-template library
        // format only — capture is driven exclusively by the candidate names the
        // execution injector stamps.
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "{COGS_instructions}",
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["COGS_instructions"] = new SignatureFieldDefinition("COGS", "", "default"),
            },
        };

        Dictionary<string, string?> inputs = new()
        {
            ["COGS_instructions"] = "resolved",
        };

        props.SetExecutionState(asPlaceHolder: false, inputs);

        Assert.NotNull(props.ExecutionState);
        Assert.Null(props.ExecutionState.ResolvedSignatureFieldValues);
    }

    [Fact]
    public void SetExecutionState_SkipsPathologicallyLargeSignatureValues()
    {
        // Arrange — one reasonable, one >64KB.
        string huge = new string('x', 64 * 1024 + 1);
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "{small} and {huge}",
            CandidateSignatureFieldNames = ["small", "huge"],
        };

        Dictionary<string, string?> inputs = new()
        {
            ["small"] = "tiny",
            ["huge"] = huge,
        };

        // Act
        props.SetExecutionState(asPlaceHolder: false, inputs);

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.NotNull(props.ExecutionState.ResolvedSignatureFieldValues);
        Assert.True(props.ExecutionState.ResolvedSignatureFieldValues.ContainsKey("small"));
        Assert.False(props.ExecutionState.ResolvedSignatureFieldValues.ContainsKey("huge"));
    }

    [Fact]
    public void UpdateExecutionState_AlsoCapturesResolvedSignatureFieldValues()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "{COGS_instructions}",
            CandidateSignatureFieldNames = ["COGS_instructions"],
        };

        Dictionary<string, string?> inputs = new()
        {
            ["COGS_instructions"] = "resolved",
        };

        // Act — UpdateExecutionState hits the same capture path.
        props.UpdateExecutionState(asPlaceHolder: false, inputs);

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.NotNull(props.ExecutionState.ResolvedSignatureFieldValues);
        Assert.Equal("resolved", props.ExecutionState.ResolvedSignatureFieldValues["COGS_instructions"]);
    }

    [Fact]
    public void SetExecutionState_SkipsEmptyCapturedValues()
    {
        // Replay can't string-replace an empty needle (CountOccurrences("") == 0),
        // so the capture path should omit empty values entirely — not record them
        // as CanReplay=true candidates that are guaranteed to 409 on Run.
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "{populated} and {blank}",
            CandidateSignatureFieldNames = ["populated", "blank"],
        };

        Dictionary<string, string?> inputs = new()
        {
            ["populated"] = "real-value",
            ["blank"] = string.Empty,
        };

        props.SetExecutionState(asPlaceHolder: false, inputs);

        Assert.NotNull(props.ExecutionState);
        Assert.NotNull(props.ExecutionState.ResolvedSignatureFieldValues);
        Assert.True(props.ExecutionState.ResolvedSignatureFieldValues.ContainsKey("populated"));
        Assert.False(props.ExecutionState.ResolvedSignatureFieldValues.ContainsKey("blank"));
    }

    [Fact]
    public void UpdateExecutionState_OverwritesStaleResolvedSignatureFieldValues()
    {
        // A re-execution via UpdateExecutionState must refresh the dict — a
        // previous run's keys should not leak through if the current inputs
        // don't populate them.
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "{keep} {drop}",
            CandidateSignatureFieldNames = ["keep", "drop"],
        };

        // First pass captures both.
        props.SetExecutionState(asPlaceHolder: false, new Dictionary<string, string?>
        {
            ["keep"] = "old-keep",
            ["drop"] = "old-drop",
        });
        Assert.True(props.ExecutionState!.ResolvedSignatureFieldValues!.ContainsKey("drop"));

        // Second pass only populates "keep" — "drop" must disappear from the dict.
        props.UpdateExecutionState(asPlaceHolder: false, new Dictionary<string, string?>
        {
            ["keep"] = "new-keep",
        });

        Assert.NotNull(props.ExecutionState.ResolvedSignatureFieldValues);
        Assert.Equal("new-keep", props.ExecutionState.ResolvedSignatureFieldValues["keep"]);
        Assert.False(props.ExecutionState.ResolvedSignatureFieldValues.ContainsKey("drop"));
    }
}
