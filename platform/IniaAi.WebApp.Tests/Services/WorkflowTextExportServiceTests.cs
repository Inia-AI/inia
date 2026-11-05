using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class WorkflowTextExportServiceTests
{
    private readonly WorkflowTextExportService _service;

    public WorkflowTextExportServiceTests()
    {
        _service = new WorkflowTextExportService();
    }

    #region ExportWorkflowToText Tests

    [Fact]
    public void ExportWorkflowToText_WithBasicWorkflow_ReturnsFormattedText()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow")
        {
            Title = "Test Report",
            Description = "Test Description"
        };

        // Act
        string result = _service.ExportWorkflowToText(workflow);

        // Assert
        Assert.Contains("# Test Report", result);
        Assert.Contains("**Workflow Name:** Test Workflow", result);
    }

    [Fact]
    public void ExportWorkflowToText_WithVariables_IncludesVariables()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");
        workflow.Variables.Add(new WorkflowVariable(workflow)
        {
            ReplacementTag = "company_name",
            DisplayName = "Company Name",
            Type = WorkflowVariableTypes.String,
            DefaultValue = "Acme Corp",
            IsRequired = true
        });

        // Act
        string result = _service.ExportWorkflowToText(workflow);

        // Assert
        Assert.Contains("## Workflow Variables", result);
        Assert.Contains("company_name", result);
        Assert.Contains("Company Name", result);
        Assert.Contains("- **Type:** String", result);
        Assert.Contains("- **Required:** Yes", result);
        Assert.Contains("- **Default:** Acme Corp", result);
    }

    [Fact]
    public void ExportWorkflowToText_WithoutVariables_ShowsNone()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");

        // Act
        string result = _service.ExportWorkflowToText(workflow);

        // Assert
        Assert.DoesNotContain("## Workflow Variables", result);
        Assert.DoesNotContain("(none)", result);
    }

    [Fact]
    public void ExportWorkflowToText_WithSections_IncludesAllSections()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");

        _ = new Section("Section 1", workflow, 0);

        _ = new Section("Section 2", workflow, 1);

        // Act
        string result = _service.ExportWorkflowToText(workflow);

        // Assert
        Assert.DoesNotContain("## Workflow Sections (Reference Only - Edit in UI)", result);
        Assert.Contains("# Section 0: Section 1", result);
        Assert.Contains("# Section 1: Section 2", result);
    }

    [Fact]
    public void ExportWorkflowToText_WithSectionsAndBlocks_IncludesBlockCounts()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Section", workflow, 0);

        _ = new Block(BlockTypes.AiQuery, section, 0) { Name = "Block 1", };

        _ = new Block(BlockTypes.SimpleRag, section, 1) { Name = "Block 2", };

        // Act
        string result = _service.ExportWorkflowToText(workflow);

        // Assert
        Assert.Contains("# Section 0: Test Section", result);
    }

    #endregion

    #region ExportSectionToText Tests

    [Fact]
    public void ExportSectionToText_WithBasicSection_ReturnsFormattedText()
    {
        // Arrange
        var parentWorkflow = new Workflow("Parent");
        var section = new Section("Test Section", parentWorkflow)
        {
            Title = "Section Title",
            Description = "Section Description"
        };

        // Act
        string result = _service.ExportSectionToText(section);

        // Assert
        Assert.Contains("# Section 0: Section Title", result);
    }

    [Fact]
    public void ExportSectionToText_WithNoBlocks_ShowsEmptyBlocks()
    {
        // Arrange
        var parentWorkflow = new Workflow("Parent");
        _ = new Section("Test Section", parentWorkflow, 0);
        Section section = parentWorkflow.Sections.First();

        // Act
        string result = _service.ExportSectionToText(section);

        // Assert
        Assert.Contains("*No blocks in this section.*", result);
    }

    [Fact]
    public void ExportSectionToText_WithBlocks_IncludesAllBlocks()
    {
        // Arrange
        var parentWorkflow = new Workflow("Parent");
        var sectionWorkflow = new Section("Test Section", parentWorkflow, 0);

        _ = new Block(BlockTypes.AiQuery, sectionWorkflow, 0)
        {
            Name = "First Block",
            Description = "First block description"
        };

        _ = new Block(BlockTypes.SimpleRag, sectionWorkflow, 1)
        {
            Name = "Second Block",
            Description = "Second block description"
        };

        Section section = parentWorkflow.Sections.First();

        // Act
        string result = _service.ExportSectionToText(section);

        // Assert
        Assert.Contains("## Block 0: First Block", result);
        Assert.Contains("## Block 1: Second Block", result);
    }

    #endregion

    #region ExportBlockToText Tests

    [Fact]
    public void ExportBlockToText_WithBasicBlock_ReturnsFormattedText()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "Test Block",
            Description = "Test Description",
            ReplacementTag = "test_block"
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("## Block 0: Test Block", result);
        Assert.Contains("**Type:** `AiQuery`", result);
        Assert.Contains("**Replacement Tag:** `test_block`", result);
    }

    [Fact]
    public void ExportBlockToText_WithAiQueryBlock_IncludesPrompts()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Query",
            Properties = new AiQueryProperties
            {
                SystemPromptTemplate = "You are a helpful assistant.",
                UserPromptTemplate = "Please analyze {InputContext}",
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### System Prompt Template", result);
        Assert.Contains("You are a helpful assistant.", result);
        Assert.Contains("### User Prompt Template", result);
        Assert.Contains("Please analyze {InputContext}", result);
    }

    [Fact]
    public void ExportBlockToText_WithTextInputs_IncludesTextInputs()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.WebApiCall, section, 0)
        {
            Name = "Test Block"
        };

        _ = new TextInput(block, "01000-httpMethod")
        {
            Value = "GET"
        };

        _ = new TextInput(block, "04000-headers")
        {
            Value = "Authorization:Bearer longsecrettoken"
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Text Inputs (Reference Only)", result);
        Assert.Contains("httpMethod: GET", result);
        // Tag contains "header" so the value must be obfuscated to keep credentials
        // out of the clipboard. The raw token must not appear anywhere in the output.
        Assert.DoesNotContain("Authorization:Bearer longsecrettoken", result);
        Assert.Contains("headers: ", result);
        Assert.Contains("*", result);
    }

    [Fact]
    public void ExportBlockToText_WithSensitiveTextInputTag_RedactsValue()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.WebApiCall, section, 0)
        {
            Name = "Creds Block"
        };

        _ = new TextInput(block, "01000-apiToken")
        {
            Value = "sk-supersecretvalue-1234"
        };

        _ = new TextInput(block, "02000-password")
        {
            Value = "p@ssw0rd"
        };

        _ = new TextInput(block, "03000-shortSecret")
        {
            Value = "abc"
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.DoesNotContain("sk-supersecretvalue-1234", result);
        Assert.DoesNotContain("p@ssw0rd", result);
        Assert.Contains("apiToken: ", result);
        Assert.Contains("password: ", result);
        // Values shorter than 5 chars collapse to the REDACTED literal.
        Assert.Contains("shortSecret: ****", result);
    }

    [Fact]
    public void ExportBlockToText_WithNonSensitiveTextInputTag_KeepsValue()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.WebApiCall, section, 0)
        {
            Name = "Plain Block"
        };

        _ = new TextInput(block, "01000-httpMethod")
        {
            Value = "POST"
        };

        _ = new TextInput(block, "02000-acceptType")
        {
            Value = "application/json"
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("httpMethod: POST", result);
        Assert.Contains("acceptType: application/json", result);
    }

    [Fact]
    public void ExportBlockToText_WithNoTextInputs_ShowsNone()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "Test Block"
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.DoesNotContain("### Text Inputs (Reference Only)", result);
    }

    [Fact]
    public void ExportBlockToText_WithExecutionState_DoesNotLeakRuntimeValues()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "Test Block",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Original prompt {InputContext}",
            },
        };
        var props = block.Properties as AiQueryProperties;
        Assert.NotNull(props);

        props.ExecutionState = new (props)
        {
            UserPrompt = "Original prompt Actual context value",
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        // Template stays; resolved runtime value (the bit that would leak document
        // contents into a clipboard pasted to an LLM) must not.
        Assert.Contains("### User Prompt Template", result);
        Assert.Contains("Original prompt {InputContext}", result);
        Assert.DoesNotContain("Actual context value", result);
    }

    [Fact]
    public void ExportBlockToText_WithRagSettings_IncludesRagConfiguration()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.SimpleRag, section, 0)
        {
            Name = "RAG Block"
        };

        block.RagSettings.Add(new RagSettings(block)
        {
            Type = RagTypes.TopNPages,
            NumberOfPagesInput = "5"
        });

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        // RAG settings are not currently exported by the service
        Assert.Contains("## Block 0: RAG Block", result);
    }

    [Fact]
    public void ExportBlockToText_WithMultipleBlockTypes_HandlesCorrectly()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));

        var aiBlock = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Test prompt",
            },
        };

        var ragBlock = new Block(BlockTypes.SimpleRag, section, 1)
        {
            Name = "RAG Block",
            Properties = new MergeProperties
            {
                TextTemplate = "RAG template",
            },
        };

        var searchBlock = new Block(BlockTypes.IntelligentSearch, section, 2)
        {
            Name = "Search Block",
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "Search term",
            },
        };

        // Act
        string aiResult = _service.ExportBlockToText(aiBlock);
        string ragResult = _service.ExportBlockToText(ragBlock);
        string searchResult = _service.ExportBlockToText(searchBlock);

        // Assert
        Assert.Contains("### User Prompt Template", aiResult);
        Assert.Contains("### Text Template", ragResult);
        Assert.Contains("### Search Term Template", searchResult);
    }

    [Fact]
    public void ExportBlockToText_WithNullValues_HandlesGracefully()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "Minimal Block",
            Description = string.Empty,
            ReplacementTag = string.Empty
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("## Block 0: Minimal Block", result);
        Assert.DoesNotContain("Description:", result);
        Assert.DoesNotContain("Replacement Tag:", result);
    }

    [Fact]
    public void ExportBlockToText_WithLongText_FormatsCorrectly()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        string longPrompt = string.Join("\n", Enumerable.Repeat("This is a long line of text.", 10));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "Long Block",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = longPrompt,
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### User Prompt Template", result);
        Assert.Contains(longPrompt, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ExportWorkflowToText_WithEmptyWorkflow_ReturnsBasicStructure()
    {
        // Arrange
        var workflow = new Workflow("Empty");

        // Act
        string result = _service.ExportWorkflowToText(workflow);

        // Assert
        Assert.Contains("# Empty", result);
        Assert.DoesNotContain("## Workflow Variables", result);
        Assert.DoesNotContain("## Workflow Sections (Reference Only - Edit in UI)", result);
    }

    [Fact]
    public void ExportSectionToText_WithNullParent_HandlesGracefully()
    {
        // Arrange
        var section = new Section { Name = "Orphan Section" };

        // Act
        string result = _service.ExportSectionToText(section);

        // Assert
        Assert.Contains("# Section 0: ", result);
    }

    [Fact]
    public void ExportBlockToText_WithAllAiQueryFields_IncludesPrompts()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "Complete Block",
            Description = "Full description",
            ReplacementTag = "complete_block",
            Properties = new AiQueryProperties
            {
                SystemPromptTemplate = "System prompt",
                UserPromptTemplate = "User prompt {InputContext}",
            },
            IsOutputSource = true,
            ShouldDisplayInLogs = true,
        };
        var props = block.Properties as AiQueryProperties;
        Assert.NotNull(props);

        block.RagSettings.Add(new RagSettings(block)
        {
            Type = RagTypes.WholeDocument,
        });

        _ = new TextInput(block, "test-input")
        {
            Value = "test value",
        };

        props.ExecutionState = new (props)
        {
            UserPrompt = "Executed user prompt",
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("## Block 0: Complete Block", result);
        Assert.Contains("AiQuery", result);
        Assert.Contains("complete_block", result);
        Assert.Contains("### System Prompt Template", result);
        Assert.Contains("System prompt", result);
        Assert.Contains("### User Prompt Template", result);
        Assert.Contains("User prompt", result);
        Assert.Contains("### Text Inputs (Reference Only)", result);
        Assert.Contains("test-input", result);
    }

    [Fact]
    public void ExportBlockToText_WithMergeProperties_IncludesTextTemplate()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Merge, section, 0)
        {
            Name = "Merge Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Text template",
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Text Template", result);
        Assert.Contains("Text template", result);
    }

    [Fact]
    public void ExportBlockToText_WithSearchProperties_IncludesSearchTerm()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.IntelligentSearch, section, 0)
        {
            Name = "Search Block",
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "Search term",
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Search Term Template", result);
        Assert.Contains("Search term", result);
    }

    #endregion

    #region AI Configuration

    [Fact]
    public void ExportBlockToText_WithAiQueryBlock_AlwaysIncludesAiConfigurationHeading()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties(),
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        // AI Configuration is core context for Claude even when at defaults.
        Assert.Contains("### AI Configuration", result);
        Assert.Contains("- **MaxTokens:** 8000", result);
        Assert.Contains("- **ShouldUseAdvancedModel:** true", result);
        Assert.Contains("- **ShouldUseThinkingMode:** false", result);
        Assert.DoesNotContain("- **ThinkingBudget:**", result);
        Assert.DoesNotContain("- **ModelIdOverride:**", result);
        Assert.DoesNotContain("- **Effort:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithAiQueryEffortSet_IncludesEffort()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                Effort = ModelEffort.XHigh,
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("- **Effort:** XHigh", result);
    }

    [Fact]
    public void ExportBlockToText_WithAiQueryThinkingModeOn_IncludesThinkingBudget()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                MaxTokens = 2048,
                ShouldUseAdvancedModel = false,
                ShouldUseThinkingMode = true,
                ThinkingBudget = 1000,
                ModelIdOverride = "claude-opus-4-7",
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("- **MaxTokens:** 2048", result);
        Assert.Contains("- **ShouldUseAdvancedModel:** false", result);
        Assert.Contains("- **ShouldUseThinkingMode:** true", result);
        Assert.Contains("- **ThinkingBudget:** 1000", result);
        Assert.Contains("- **ModelIdOverride:** `claude-opus-4-7`", result);
    }

    [Fact]
    public void ExportBlockToText_WithNonAiBlock_DoesNotIncludeAiConfiguration()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Merge, section, 0)
        {
            Name = "Merge Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Hello",
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.DoesNotContain("### AI Configuration", result);
    }

    #endregion

    #region Signature Fields

    [Fact]
    public void ExportBlockToText_WithAiQuerySignatureFields_IncludesSignatureFieldsSection()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["COGS_instructions"] = new("COGS instructions", "Help text", "Default COGS guidance"),
                    ["RevRec_instructions"] = new("Revenue rec instructions", "More help", string.Empty),
                },
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Signature Fields", result);
        Assert.Contains("- `COGS_instructions`: Default COGS guidance", result);
        Assert.Contains("- `RevRec_instructions`: (no default)", result);
        Assert.DoesNotContain("COGS instructions", result);
        Assert.DoesNotContain("Help text", result);
    }

    [Fact]
    public void ExportBlockToText_WithMergeSignatureFields_IncludesSignatureFieldsSection()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Merge, section, 0)
        {
            Name = "Merge Block",
            Properties = new MergeProperties
            {
                TextTemplate = "{COGS_instructions}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["COGS_instructions"] = new("COGS", "Help", "Default COGS"),
                },
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Signature Fields", result);
        Assert.Contains("- `COGS_instructions`: Default COGS", result);
    }

    [Fact]
    public void ExportBlockToText_WithNoSignatureFields_DoesNotIncludeSignatureFieldsSection()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties(),
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.DoesNotContain("### Signature Fields", result);
    }

    #endregion

    #region Newly Covered Block Property Types

    [Fact]
    public void ExportBlockToText_WithStringManipulationReplaceOp_IncludesReplaceFields()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "String Block",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "Hello {name}",
                Operation = StringManipulationOperation.Replace,
                Find = "Hello",
                ReplaceWith = "Hi",
                CaseSensitive = false,
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Input Template", result);
        Assert.Contains("Hello {name}", result);
        Assert.Contains("### String Manipulation", result);
        Assert.Contains("- **Operation:** Replace", result);
        Assert.Contains("- **Find:** `Hello`", result);
        Assert.Contains("- **ReplaceWith:** `Hi`", result);
        Assert.Contains("- **CaseSensitive:** false", result);
        Assert.DoesNotContain("- **StartIndex:**", result);
        Assert.DoesNotContain("- **TrimSide:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationSubstringOp_IncludesSubstringFields()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Substring Block",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "abcdef",
                Operation = StringManipulationOperation.Substring,
                StartIndex = 1,
                Length = 3,
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("- **Operation:** Substring", result);
        Assert.Contains("- **StartIndex:** 1", result);
        Assert.Contains("- **Length:** 3", result);
        Assert.DoesNotContain("- **Find:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithMergeJsonProperties_IncludesJsonInputsAndExtractions()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.MergeJson, section, 0)
        {
            Name = "MergeJson Block",
            Properties = new MergeJsonProperties
            {
                JsonInputs = ["{\"a\":1}", "{\"b\":2}"],
                InputExtractions =
                [
                    new MergeJsonInputExtraction { Mode = JsonExtractionMode.Single, Pattern = "*" },
                    new MergeJsonInputExtraction { Mode = JsonExtractionMode.XmlTagPattern, Pattern = "page*" },
                ],
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### JSON Inputs", result);
        Assert.Contains("#### Input 0", result);
        Assert.Contains("{\"a\":1}", result);
        Assert.Contains("#### Input 1", result);
        Assert.Contains("{\"b\":2}", result);
        Assert.Contains("### Input Extractions", result);
        Assert.Contains("- **Input 0:** Mode=`Single`, Pattern=`*`", result);
        Assert.Contains("- **Input 1:** Mode=`XmlTagPattern`, Pattern=`page*`", result);
    }

    [Fact]
    public void ExportBlockToText_WithExchangeRateProperties_IncludesExchangeRateFields()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.ExchangeRate, section, 0)
        {
            Name = "FX Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "USD",
                ToCurrency = "EUR",
                UseCurrentDate = false,
                ExchangeDate = "2024-01-15",
                InputValue = "100",
                OperationMode = ExchangeRateOperationMode.Convert,
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Exchange Rate", result);
        Assert.Contains("- **OperationMode:** Convert", result);
        Assert.Contains("- **FromCurrency:** `USD`", result);
        Assert.Contains("- **ToCurrency:** `EUR`", result);
        Assert.Contains("- **UseCurrentDate:** false", result);
        Assert.Contains("- **ExchangeDate:** `2024-01-15`", result);
        Assert.Contains("- **InputValue:** `100`", result);
    }

    [Fact]
    public void ExportBlockToText_WithDynamicChartProperties_IncludesChartConfiguration()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.DynamicChart, section, 0)
        {
            Name = "Chart Block",
            Properties = new DynamicChartProperties
            {
                ChartType = "bar",
                ChartTitle = "Sales by Month",
                XAxisField = "month",
                YAxisField = "sales",
                DataSourceJson = "[{\"month\":\"Jan\",\"sales\":100}]",
                DataFilters = "[{\"field\":\"region\"}]",
                ShowLegend = false,
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Chart Configuration", result);
        Assert.Contains("- **ChartType:** `bar`", result);
        Assert.Contains("- **ChartTitle:** `Sales by Month`", result);
        Assert.Contains("- **XAxisField:** `month`", result);
        Assert.Contains("- **YAxisField:** `sales`", result);
        Assert.Contains("- **ShowLegend:** false", result);
        Assert.Contains("- **DataFilters:** `[{\"field\":\"region\"}]`", result);
        Assert.Contains("### Data Source JSON", result);
        Assert.Contains("[{\"month\":\"Jan\",\"sales\":100}]", result);
    }

    [Fact]
    public void ExportBlockToText_WithDynamicChartDefaultDataFilters_OmitsDataFilters()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.DynamicChart, section, 0)
        {
            Name = "Chart Block",
            Properties = new DynamicChartProperties(),
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.DoesNotContain("- **DataFilters:**", result);
        Assert.DoesNotContain("### Data Source JSON", result);
    }

    [Fact]
    public void ExportBlockToText_WithRetrieveDataFromDocumentsOverviewsOn_IncludesRetrievalConfig()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.SimpleRag, section, 0)
        {
            Name = "Retrieve Block",
            Properties = new RetrieveDataFromDocumentsProperties
            {
                ShouldUseOverviews = true,
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.Contains("### Retrieval Configuration", result);
        Assert.Contains("- **ShouldUseOverviews:** true", result);
    }

    [Fact]
    public void ExportBlockToText_WithRetrieveDataFromDocumentsOverviewsOff_OmitsRetrievalConfig()
    {
        // Arrange
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.SimpleRag, section, 0)
        {
            Name = "Retrieve Block",
            Properties = new RetrieveDataFromDocumentsProperties
            {
                ShouldUseOverviews = false,
            },
        };

        // Act
        string result = _service.ExportBlockToText(block);

        // Assert
        Assert.DoesNotContain("### Retrieval Configuration", result);
    }

    #endregion

    #region StringManipulation Operation Coverage

    [Fact]
    public void ExportBlockToText_WithStringManipulationTrimOp_IncludesTrimFields()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Trim Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.Trim,
                TrimSide = StringManipulationTrimSide.Start,
                TrimCharacters = " \t",
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** Trim", result);
        Assert.Contains("- **TrimSide:** Start", result);
        Assert.Contains("- **TrimCharacters:** ` \t`", result);
        Assert.DoesNotContain("- **Find:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationSplitOp_IncludesSeparatorAndIndex()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Split Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.Split,
                Separator = ",",
                SplitIndex = 2,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** Split", result);
        Assert.Contains("- **Separator:** `,`", result);
        Assert.Contains("- **SplitIndex:** 2", result);
        Assert.DoesNotContain("- **Find:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationJoinOp_IncludesSeparatorOnly()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Join Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.Join,
                Separator = "|",
                SplitIndex = 5,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** Join", result);
        Assert.Contains("- **Separator:** `|`", result);
        // Join ignores SplitIndex, so it must not be emitted under the Join branch.
        Assert.DoesNotContain("- **SplitIndex:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationChangeCaseOp_IncludesCaseType()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Case Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.ChangeCase,
                CaseType = StringManipulationCaseType.Upper,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** ChangeCase", result);
        Assert.Contains("- **CaseType:** Upper", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationEscapeOp_IncludesAllEscapeFlags()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Escape Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.EscapeSpecialCharacters,
                EscapeNewlines = true,
                EscapeTabs = false,
                EscapeCarriageReturns = true,
                EscapeBackslashes = false,
                EscapeDoubleQuotes = true,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** EscapeSpecialCharacters", result);
        Assert.Contains("- **EscapeNewlines:** true", result);
        Assert.Contains("- **EscapeTabs:** false", result);
        Assert.Contains("- **EscapeCarriageReturns:** true", result);
        Assert.Contains("- **EscapeBackslashes:** false", result);
        Assert.Contains("- **EscapeDoubleQuotes:** true", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationUnescapeOp_IncludesAllEscapeFlags()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Unescape Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.UnescapeSpecialCharacters,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** UnescapeSpecialCharacters", result);
        Assert.Contains("- **EscapeNewlines:**", result);
        Assert.Contains("- **EscapeDoubleQuotes:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationReplaceUsingPatternOp_IncludesFindAndReplaceWith()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Pattern Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.ReplaceUsingPattern,
                Find = "\\d+",
                ReplaceWith = "N",
                CaseSensitive = true,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** ReplaceUsingPattern", result);
        Assert.Contains("- **Find:** `\\d+`", result);
        Assert.Contains("- **ReplaceWith:** `N`", result);
        Assert.Contains("- **CaseSensitive:** true", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationLengthOp_OnlyEmitsOperationLine()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Length Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.Length,
                Find = "ignored",
                Separator = "ignored",
                CaseType = StringManipulationCaseType.Title,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** Length", result);
        Assert.DoesNotContain("- **Find:**", result);
        Assert.DoesNotContain("- **Separator:**", result);
        Assert.DoesNotContain("- **CaseType:**", result);
        Assert.DoesNotContain("- **TrimSide:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithStringManipulationDistinctOp_OnlyEmitsOperationLine()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.StringManipulation, section, 0)
        {
            Name = "Distinct Block",
            Properties = new StringManipulationProperties
            {
                Operation = StringManipulationOperation.Distinct,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Operation:** Distinct", result);
        Assert.DoesNotContain("- **Find:**", result);
        Assert.DoesNotContain("- **Separator:**", result);
    }

    #endregion

    #region AiQuery Output Format + Conditional Tag

    [Fact]
    public void ExportBlockToText_WithOutputFormatNone_OmitsOutputFormatSection()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                OutputFormatType = OutputFormatType.None,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.DoesNotContain("### Output Format Validation", result);
    }

    [Fact]
    public void ExportBlockToText_WithOutputFormatSingleTag_IncludesTagName()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                OutputFormatType = OutputFormatType.SingleTag,
                OutputFormatTagName = "answer",
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("### Output Format Validation", result);
        Assert.Contains("- **OutputFormatType:** SingleTag", result);
        Assert.Contains("- **OutputFormatTagName:** `answer`", result);
        Assert.DoesNotContain("OutputFormatTagNames:", result);
        Assert.DoesNotContain("Output Format JSON Schema", result);
    }

    [Fact]
    public void ExportBlockToText_WithOutputFormatMultiTag_IncludesTagNames()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                OutputFormatType = OutputFormatType.MultiTag,
                OutputFormatTagNames = "reasoning,answer",
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **OutputFormatType:** MultiTag", result);
        Assert.Contains("- **OutputFormatTagNames:** `reasoning,answer`", result);
    }

    [Fact]
    public void ExportBlockToText_WithOutputFormatJsonInTag_IncludesTagAndSchema()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                OutputFormatType = OutputFormatType.JsonInTag,
                OutputFormatTagName = "result",
                OutputFormatJsonSchema = "{\"type\":\"object\"}",
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **OutputFormatType:** JsonInTag", result);
        Assert.Contains("- **OutputFormatTagName:** `result`", result);
        Assert.Contains("#### Output Format JSON Schema", result);
        Assert.Contains("{\"type\":\"object\"}", result);
    }

    [Fact]
    public void ExportBlockToText_WithConditionalTagTemplate_IncludesSection()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                ConditionalTagTemplate = "answer",
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("### Conditional Tag Template", result);
        Assert.Contains("answer", result);
    }

    [Fact]
    public void ExportBlockToText_WithoutConditionalTagTemplate_OmitsSection()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties(),
        };

        string result = _service.ExportBlockToText(block);

        Assert.DoesNotContain("### Conditional Tag Template", result);
    }

    #endregion

    #region SwitchCase

    [Fact]
    public void ExportBlockToText_WithSwitchCaseCasesAndDefault_IncludesAllRoutes()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.SwitchCase, section, 0)
        {
            Name = "Switch Block",
            Properties = new SwitchCaseProperties
            {
                InputTemplate = "{value}",
                Cases = new Dictionary<string, string>
                {
                    ["red"] = "block_a",
                    ["blue"] = "block_b",
                },
                DefaultValue = "block_fallback",
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("### Text Template", result);
        Assert.Contains("{value}", result);
        Assert.Contains("### Switch Cases", result);
        Assert.Contains("- `red` -> block_a", result);
        Assert.Contains("- `blue` -> block_b", result);
        Assert.Contains("- **Default:** block_fallback", result);
    }

    [Fact]
    public void ExportBlockToText_WithSwitchCaseNoCasesNoDefault_OmitsSwitchCasesSection()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.SwitchCase, section, 0)
        {
            Name = "Switch Block",
            Properties = new SwitchCaseProperties(),
        };

        string result = _service.ExportBlockToText(block);

        Assert.DoesNotContain("### Switch Cases", result);
    }

    #endregion

    #region DynamicChart Noise Reduction

    [Fact]
    public void ExportBlockToText_WithDynamicChartAtDefaults_OmitsDefaultedFields()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.DynamicChart, section, 0)
        {
            Name = "Default Chart",
            Properties = new DynamicChartProperties(),
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("### Chart Configuration", result);
        Assert.Contains("- **ChartType:** `line`", result);
        // Default-valued fields should not pollute the output.
        Assert.DoesNotContain("- **ChartTitle:**", result);
        Assert.DoesNotContain("- **XAxisLabel:**", result);
        Assert.DoesNotContain("- **YAxisLabel:**", result);
        Assert.DoesNotContain("- **Stacking:**", result);
        Assert.DoesNotContain("- **ShowLegend:**", result);
        Assert.DoesNotContain("- **ShowGridLines:**", result);
        Assert.DoesNotContain("- **EnableAnimations:**", result);
        Assert.DoesNotContain("- **DataFilters:**", result);
    }

    [Fact]
    public void ExportBlockToText_WithDynamicChartCustomLabels_EmitsOnlyCustomisedFields()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.DynamicChart, section, 0)
        {
            Name = "Custom Chart",
            Properties = new DynamicChartProperties
            {
                ChartType = "bar",
                ChartTitle = "Sales",
                XAxisLabel = "Month",
                YAxisLabel = "Y Axis",
                Stacking = "normal",
                ShowLegend = false,
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **ChartType:** `bar`", result);
        Assert.Contains("- **ChartTitle:** `Sales`", result);
        Assert.Contains("- **XAxisLabel:** `Month`", result);
        Assert.Contains("- **Stacking:** `normal`", result);
        Assert.Contains("- **ShowLegend:** false", result);
        // YAxisLabel is still the default and must not appear.
        Assert.DoesNotContain("- **YAxisLabel:**", result);
    }

    #endregion

    #region Signature Robustness

    [Fact]
    public void ExportBlockToText_WithEmptySignatureFieldsDictionary_OmitsSignatureFieldsSection()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                SignatureFields = [],
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.DoesNotContain("### Signature Fields", result);
    }

    [Fact]
    public void ExportBlockToText_WithNullSignatureFieldDefinitionEntry_SkipsItGracefully()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.AiQuery, section, 0)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties
            {
                // Same shape JSON deserialisation can produce — record reference can land null
                // even though the param type is non-nullable.
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["valid_tag"] = new("Valid", "Help", "Valid default"),
                    ["null_tag"] = null!,
                },
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- `valid_tag`: Valid default", result);
        Assert.DoesNotContain("null_tag", result);
    }

    #endregion

    #region Agent Family Configuration

    [Fact]
    public void ExportBlockToText_WithAgentBlockAllFieldsSet_IncludesPromptsModelEffortFusesAndSkills()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Agent, section, 0)
        {
            Name = "Agent Block",
            Properties = new AgentProperties
            {
                PromptTemplate = "Do the work {Input}",
                SystemPromptTemplate = "You are precise",
                Model = "claude-sonnet-5",
                Effort = "high",
                DeadlineMinutes = 20,
                MaxBudgetUsd = 1.50m,
                MaxTurns = 40,
                MaxRetainedOutputMib = 8,
                WorkerSkills =
                [
                    new AgentWorkerSkill
                    {
                        Name = "footing",
                        Description = "checks footings",
                        Content = "run the script",
                    },
                ],
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("### User Prompt Template", result);
        Assert.Contains("Do the work {Input}", result);
        Assert.Contains("### System Prompt Template", result);
        Assert.Contains("You are precise", result);
        Assert.Contains("### Agent Configuration", result);
        Assert.Contains("- **Model:** `claude-sonnet-5`", result);
        Assert.Contains("- **ShouldUseAdvancedModel:** true", result);
        Assert.Contains("- **Effort:** `high`", result);
        Assert.Contains("- **Deadline (minutes):** 20", result);
        // decimal preserves scale, so 1.50m renders as "1.50" — assert the exact render.
        Assert.Contains("- **Max Budget (USD):** 1.50", result);
        Assert.Contains("- **Max Turns:** 40", result);
        Assert.Contains("- **Max Retained Output (MiB):** 8", result);
        Assert.Contains("### Worker Skills", result);
        Assert.Contains("#### footing", result);
        Assert.Contains("checks footings", result);
        Assert.Contains("run the script", result);
    }

    [Fact]
    public void ExportBlockToText_WithVerificationBlock_IncludesAgentConfigMembersCriteriaAndPassCap()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Verification, section, 0)
        {
            Name = "Verify Block",
            Properties = new VerificationProperties
            {
                PromptTemplate = "Judge it",
                ShouldUseAdvancedModel = false,
                DeadlineMinutes = 15,
                MaxBudgetUsd = 2m,
                PassCap = 3,
                Members =
                [
                    new VerificationMember
                    {
                        MemberTag = "01-extract",
                        RedoMode = VerificationRedoMode.Scratch,
                    },
                ],
                Criteria =
                [
                    new VerificationCriterion
                    {
                        Id = "footing_ties",
                        Label = "Footings tie",
                        AppliesTo = ["01-extract"],
                    },
                    new VerificationCriterion { Id = "cross_check", Label = "Cross member" },
                ],
                WorkerSkills = [new AgentWorkerSkill { Name = "rubric", Content = "how to judge" }],
            },
        };

        string result = _service.ExportBlockToText(block);

        // Shared agent-family arm still fires for the verification block; a blank model prints
        // no Model line, and the tier says which model runs.
        Assert.Contains("### Agent Configuration", result);
        Assert.DoesNotContain("- **Model:**", result);
        Assert.Contains("- **ShouldUseAdvancedModel:** false", result);
        Assert.Contains("- **Deadline (minutes):** 15", result);
        // Verification-specific group.
        Assert.Contains("### Verification", result);
        Assert.Contains("- **Pass Cap:** 3", result);
        Assert.Contains("#### Members", result);
        Assert.Contains("- `01-extract` — redo mode `Scratch`", result);
        Assert.Contains("#### Criteria", result);
        Assert.Contains("- `footing_ties`: Footings tie (applies to: 01-extract)", result);
        Assert.Contains("- `cross_check`: Cross member (applies to: group)", result);
        Assert.Contains("### Worker Skills", result);
        Assert.Contains("#### rubric", result);
    }

    [Fact]
    public void ExportBlockToText_WithAgentBlockNoFusesDeclared_ReadsAsRuntimeDefaultNotBlankOrZero()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Agent, section, 0)
        {
            Name = "Bare Agent",
            Properties = new AgentProperties { PromptTemplate = "Do it" },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Deadline (minutes):** (runtime default)", result);
        Assert.Contains("- **Max Budget (USD):** (runtime default)", result);
        Assert.Contains("- **Max Turns:** (runtime default)", result);
        Assert.Contains("- **Max Retained Output (MiB):** (runtime default)", result);
        // No fuse reads as a zero, and an undeclared model/effort emits no line at all.
        Assert.DoesNotContain("- **Max Turns:** 0", result);
        Assert.DoesNotContain("- **Deadline (minutes):** 0", result);
        Assert.DoesNotContain("- **Model:**", result);
        Assert.DoesNotContain("- **Effort:**", result);
        Assert.DoesNotContain("Output Files", result);
    }

    [Fact]
    public void ExportBlockToText_WithAgentOutputFiles_ListsThem()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Agent, section, 0)
        {
            Name = "Agent Block",
            Properties = new AgentProperties
            {
                PromptTemplate = "Do it",
                OutputFiles = ["out/rows.json", "out/checks/totals.json"],
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("- **Output Files:** `out/rows.json, out/checks/totals.json`", result);
    }

    [Fact]
    public void ExportBlockToText_WithNonAgentBlock_DoesNotIncludeAgentConfiguration()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Merge, section, 0)
        {
            Name = "Merge Block",
            Properties = new MergeProperties { TextTemplate = "Hello" },
        };

        string result = _service.ExportBlockToText(block);

        Assert.DoesNotContain("### Agent Configuration", result);
        Assert.DoesNotContain("### Verification", result);
    }

    [Fact]
    public void ExportBlockToText_WithAgentBlockOutputSchema_IncludesOutputSchemaSection()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Agent, section, 0)
        {
            Name = "Agent Block",
            Properties = new AgentProperties
            {
                PromptTemplate = "Do it",
                OutputSchema = "{\"type\":\"object\",\"properties\":{\"figure\":{\"type\":\"string\"}}}",
            },
        };

        string result = _service.ExportBlockToText(block);

        Assert.Contains("### Output Schema", result);
        Assert.Contains("\"figure\"", result);
    }

    [Fact]
    public void ExportBlockToText_WithAgentBlockNoOutputSchema_OmitsOutputSchemaSection()
    {
        var section = new Section("Test", new Workflow("Test"));
        var block = new Block(BlockTypes.Agent, section, 0)
        {
            Name = "Agent Block",
            Properties = new AgentProperties { PromptTemplate = "Do it" },
        };

        string result = _service.ExportBlockToText(block);

        Assert.DoesNotContain("### Output Schema", result);
    }

    #endregion
}
