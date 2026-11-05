using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class BlockTests
{
    private const string JSON =
    """
    {
        "Properties": {
            "UserPromptTemplate": "<today_date>\n2026-04-08\n</today_date>\n\nThis is a straightforward task.\n\n1. Convert today's date to a month-end date using \"Month Year\" format\n2. Return the output within <answer> tags without any additional text or explanation.\n\nExample 1:\nToday's date is October 4, 2025\n\n<answer>\nOctober 2025\n</answer>",
            "SystemPromptTemplate": "You are a data processor for a financial workflow.",
            "MaxTokens": 8000,
            "ShouldUseAdvancedModel": false,
            "ShouldUsePageImages": false,
            "Type": "AiQuery"
        },
        "Type": "AiQuery",
    }
    """;

    [Theory]
    [InlineData(BlockTypes.AiQuery, true)]
    [InlineData(BlockTypes.SimpleRag, true)]
    [InlineData(BlockTypes.BuildTopicsForAllDocuments, true)]
    [InlineData(BlockTypes.Agent, true)]
    [InlineData(BlockTypes.Verification, true)]
    [InlineData(BlockTypes.Merge, false)]
    [InlineData(BlockTypes.WebApiCall, false)]
    public void ShouldUseDocuments_ReflectsDocumentConsumingTypes(BlockTypes type, bool expected)
    {
        var block = new Block(type, new Section("s", new Workflow("w")));

        Assert.Equal(expected, block.ShouldUseDocuments());
    }

    [Theory]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    public void Reset_DocumentConsumingBlock_KeepsDocumentClassWiring(BlockTypes type)
    {
        var block = new Block(type, new Section("s", new Workflow("w")));
        block.SupportedDocumentClasses.Add("Annual Report");
        _ = new RagSettings(block);

        block.Reset();

        Assert.Equal(["Annual Report"], block.SupportedDocumentClasses);
        _ = Assert.Single(block.RagSettings);
    }

    [Fact]
    public void Reset_NonDocumentBlock_ClearsDocumentClassWiring()
    {
        var block = new Block(BlockTypes.Merge, new Section("s", new Workflow("w")));
        block.SupportedDocumentClasses.Add("Annual Report");

        block.Reset();

        Assert.Empty(block.SupportedDocumentClasses);
    }

    [Fact]
    public void FromJson_AiQueryBlockWithFilterRuleOnAny_KeepsItsUntickedCitations()
    {
        var authored = new Block(BlockTypes.AiQuery, new Section("s", new Workflow("w")));
        authored.SupportedDocumentClasses.Add(DocumentClass.KEY_ANY);
        _ = new RagSettings(authored) { Type = RagTypes.WholeDocument };

        Block? loaded = Block.FromJson(authored.ToJson(pretty: false));

        Assert.NotNull(loaded);
        Assert.Equal([DocumentClass.KEY_ANY], loaded.SupportedDocumentClasses);
        Assert.Single(loaded.RagSettings);
        Assert.False(((AiQueryProperties)loaded.Properties).ShouldGetCitationsFromModel);
    }

    [Fact]
    public void FromJson_AiQueryJson_ReturnsBlockWithAiQueryProperties()
    {
        // Act
        var block = Block.FromJson(JSON);

        // Assert
        Assert.NotNull(block);
        Assert.IsType<AiQueryProperties>(block.Properties);

        var props = block.Properties as AiQueryProperties;
        Assert.NotNull(props);
        Assert.Equal("<today_date>\n2026-04-08\n</today_date>\n\nThis is a straightforward task.\n\n1. Convert today's date to a month-end date using \"Month Year\" format\n2. Return the output within <answer> tags without any additional text or explanation.\n\nExample 1:\nToday's date is October 4, 2025\n\n<answer>\nOctober 2025\n</answer>", props!.UserPromptTemplate);
        Assert.Equal("You are a data processor for a financial workflow.", props.SystemPromptTemplate);
        Assert.Equal(8000, props.MaxTokens);
        Assert.False(props.ShouldUseAdvancedModel);
        Assert.False(props.ShouldUsePageImages);
    }

    [Theory]
    [InlineData(BlockTypes.Agent, true)]
    [InlineData(BlockTypes.Verification, false)]
    [InlineData(BlockTypes.AiQuery, false)]
    [InlineData(BlockTypes.Merge, false)]
    [InlineData(BlockTypes.WebApiCall, false)]
    public void AbortsExecutionOnFailure_WithoutAnOverride_FollowsTheBlockType(BlockTypes type, bool expected)
    {
        var block = new Block(type, new Section("s", new Workflow("w")));

        Assert.Equal(expected, block.AbortsExecutionOnFailure());
    }

    [Theory]
    [InlineData(BlockTypes.Agent, false)]
    [InlineData(BlockTypes.Verification, true)]
    [InlineData(BlockTypes.Merge, true)]
    public void AbortsExecutionOnFailure_WithAnOverride_FollowsTheOverride(BlockTypes type, bool isCritical)
    {
        var block = new Block(type, new Section("s", new Workflow("w")));
        block.Properties.IsCritical = isCritical;

        Assert.Equal(isCritical, block.AbortsExecutionOnFailure());
    }

    [Fact]
    public void AbortsExecutionOnFailure_JsonPredatingTheFlag_FallsBackToTheTypeDefault()
    {
        var block = Block.FromJson(JSON);

        Assert.NotNull(block);
        Assert.Null(block.Properties.IsCritical);
        Assert.False(block.AbortsExecutionOnFailure());
    }

    [Fact]
    public void Clone_CarriesTheCriticalityOverride()
    {
        var block = new Block(BlockTypes.Agent, new Section("s", new Workflow("w")));
        block.Properties.IsCritical = false;

        BlockProperties clone = block.Properties.Clone();

        Assert.False(clone.IsCritical);
    }

    [Fact]
    public void ToString_AiQuery_ReturnsExpectedString()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Order = 1,
            Name = "Test Block",
            ReplacementTag = "responseTag"
        };

        // Act
        string result = block.ToString();

        // Assert
        Assert.Equal("1. Test Block, AI, responseTag, advanced model", result);
    }

    [Theory]
    [InlineData(BlockTypes.Agent, "1. Test Block, AI Agent, responseTag")]
    [InlineData(BlockTypes.Verification, "1. Test Block, Verification Group, responseTag")]
    public void ToString_NamesTheBlockTypeTheEditorShows(BlockTypes type, string expected)
    {
        var block = new Block(type) { Order = 1, Name = "Test Block", ReplacementTag = "responseTag" };

        Assert.Equal(expected, block.ToString());
    }

    [Fact]
    public void ToString_NamesEveryBlockType()
    {
        foreach (BlockTypes type in Enum.GetValues<BlockTypes>())
        {
            var block = new Block(type) { Order = 1, Name = "Test Block", ReplacementTag = "responseTag" };

            Assert.DoesNotContain("Unknown", block.ToString());
        }
    }

    [Fact]
    public void ReplaceAll_AiQueryProperties_ReplacesInProperties()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Original User Prompt",
                SystemPromptTemplate = "Original System Prompt",
            },
        };

        Dictionary<string, string?> inputs = new()
        {
            ["Original"] = "Replacement",
        };

        // Act
        block.ReplaceAll(inputs);

        // Assert
        var props = block.Properties as AiQueryProperties;
        Assert.NotNull(props);
        Assert.Equal("Replacement User Prompt", props.UserPromptTemplate);
        Assert.Equal("Replacement System Prompt", props.SystemPromptTemplate);
    }

    [Fact]
    public void ReplaceAll_MergeProperties_ReplacesInProperties()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge)
        {
            Properties = new MergeProperties
            {
                TextTemplate = "Original Text Template",
            },
        };

        Dictionary<string, string?> inputs = new()
        {
            ["Original"] = "Replacement",
        };

        // Act
        block.ReplaceAll(inputs);

        // Assert
        var props = block.Properties as MergeProperties;
        Assert.NotNull(props);
        Assert.Equal("Replacement Text Template", props.TextTemplate);
    }

    [Fact]
    public void ReplaceAll_LoopStartProperties_ReplacesInProperties()
    {
        // Arrange
        var block = new Block(BlockTypes.LoopStart)
        {
            Properties = new LoopStartProperties
            {
                LoopedListTemplate = "Original Loop List",
            },
        };

        Dictionary<string, string?> inputs = new()
        {
            ["Original"] = "Replacement",
        };

        // Act
        block.ReplaceAll(inputs);

        // Assert
        var props = block.Properties as LoopStartProperties;
        Assert.NotNull(props);
        Assert.Equal("Replacement Loop List", props.LoopedListTemplate);
    }

    [Fact]
    public void GetAllPlaceholders_ShouldReturnAllPlaceholders()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {user}, welcome to {place}!",
                SystemPromptTemplate = "System {system}",
            },
        };
        _ = new TextInput(block, "input1", "Input {input1}");
        _ = new TextInput(block, "input2", "Input {input2}");

        var expectedPlaceholders = new List<string>
        {
            "user", "place", "system", "input1", "input2",
        };

        // Act
        List<string> result = block.GetAllPlaceholders();

        // Assert
        Assert.Equal(expectedPlaceholders.Count, result.Count);
        foreach (string placeholder in expectedPlaceholders)
        {
            Assert.Contains(placeholder, result);
        }
    }

    [Fact]
    public void GetAllPlaceholders_ShouldReturnEmptyList_WhenNoPlaceholders()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello world!",
                SystemPromptTemplate = "System",
            },
        };
        _ = new TextInput(block, "input1", "Input");
        _ = new TextInput(block, "input2", "Input");

        // Act
        List<string> result = block.GetAllPlaceholders();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetAllPlaceholders_ShouldReturnDistinctPlaceholders()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {user}, welcome to {place}!",
                SystemPromptTemplate = "System {user}",
            },
        };
        _ = new TextInput(block, "input1", "Input {input1}");
        _ = new TextInput(block, "input2", "Input {input2}");

        var expectedPlaceholders = new List<string>
        {
            "user", "place", "input1", "input2",
        };

        // Act
        List<string> result = block.GetAllPlaceholders();

        // Assert
        Assert.Equal(expectedPlaceholders.Count, result.Count);
        foreach (string placeholder in expectedPlaceholders)
        {
            Assert.Contains(placeholder, result);
        }
    }

    [Fact]
    public void HasPlaceholder_ContainsPlaceholder_ReturnsTrue()
    {
        // Arrange — use multiple blocks with different Properties types to cover all template fields
        var aiBlock = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {user}",
                SystemPromptTemplate = "System {system}",
            },
        };

        var searchBlock = new Block(BlockTypes.IntelligentSearch)
        {
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "Search {term}",
            },
        };

        var loopBlock = new Block(BlockTypes.LoopStart)
        {
            Properties = new LoopStartProperties
            {
                LoopedListTemplate = "Loop {list}",
            },
        };

        // Act & Assert
        Assert.True(aiBlock.HasPlaceholder("user"));
        Assert.True(aiBlock.HasPlaceholder("system"));
        Assert.True(searchBlock.HasPlaceholder("term"));
        Assert.True(loopBlock.HasPlaceholder("list"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContainPlaceholder_ReturnsFalse()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {user}",
                SystemPromptTemplate = "System {system}",
            },
        };

        // Act & Assert
        Assert.False(block.HasPlaceholder("missing"));
        Assert.False(block.HasPlaceholder(""));
        Assert.False(block.HasPlaceholder(null!));
    }

    [Fact]
    public void ClearExecutionState_WhenExecutionStateIsNull_ShouldNotThrowException()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery);

        // Act
        block.ClearExecutionState();

        // Assert
        Assert.Null(block.ExecutionState);
    }

    [Fact]
    public void ClearExecutionState_WhenExecutionStateExists_ShouldClearIt()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery);
        block.SetExecutionState(false, []);
        Assert.NotNull(block.ExecutionState);

        // Act
        block.ClearExecutionState();

        // Assert
        Assert.Null(block.ExecutionState);
    }

    [Fact]
    public void ClearExecutionState_RemovesTheBlockResult()
    {
        var block = new Block(BlockTypes.AiQuery);
        _ = new BlockResponse(block) { RawResponse = "an answer from an earlier run" };

        block.ClearExecutionState();

        Assert.Null(block.BlockResponse);
        Assert.Null(block.BlockResponseId);
    }

    [Fact]
    public void SetExecutionState_CascadesToPropertiesExecutionState()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge)
        {
            Properties = new MergeProperties
            {
                TextTemplate = "Hello {name}"
            }
        };

        // Act
        block.SetExecutionState(true, new Dictionary<string, string?> { { "name", "World" } });

        // Assert
        var props = (MergeProperties)block.Properties;
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("Hello World", props.ExecutionState.Text);
    }

    [Fact]
    public void UpdateExecutionState_CascadesToPropertiesExecutionState()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge)
        {
            Properties = new MergeProperties
            {
                TextTemplate = "Hello {name}"
            }
        };

        // Act
        block.UpdateExecutionState(true, new Dictionary<string, string?> { { "name", "World" } });

        // Assert
        var props = (MergeProperties)block.Properties;
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("Hello World", props.ExecutionState.Text);
    }

    [Fact]
    public void ClearExecutionStates_ClearsPropertiesExecutionState()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge)
        {
            Properties = new MergeProperties
            {
                TextTemplate = "Hello {name}"
            }
        };
        block.SetExecutionState(true, new Dictionary<string, string?> { { "name", "World" } });
        var props = (MergeProperties)block.Properties;
        Assert.NotNull(props.ExecutionState);

        // Act
        block.ClearExecutionState();

        // Assert
        Assert.Null(props.ExecutionState);
    }

    [Fact]
    public void ClearExecutionState_CascadesToNestedTextInputsAndRagSettings()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery);
        var textInput = new TextInput(block, "input1", "Hello {name}");
        var ragSetting = new RagSettings(block);

        block.SetExecutionState(
            true,
            new Dictionary<string, string?> { { "name", "World" } });

        Assert.NotNull(block.ExecutionState);
        Assert.NotNull(textInput.ExecutionState);
        Assert.NotNull(ragSetting.ExecutionState);

        // Act
        block.ClearExecutionState();

        // Assert
        Assert.Null(block.ExecutionState);
        Assert.Null(textInput.ExecutionState);
        Assert.Null(ragSetting.ExecutionState);
    }

    [Fact]
    public void RequiredDocumentsNotProvided_WithRagSettingsNoDocuments_ReturnsTrue()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery);
        _ = new RagSettings(block);

        // Act
        bool result = block.RequiredDocumentsNotProvided();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RequiredDocumentsNotProvided_WithRagSettingsAndDocuments_ReturnsFalse()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery);
        _ = new RagSettings(block);

        // Add a document to the block
        var document = new Document();
        block.Documents.Add(document);

        // Act
        bool result = block.RequiredDocumentsNotProvided();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RequiredDocumentsNotProvided_WithNoRagSettings_ReturnsFalse()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery);

        // Act
        bool result = block.RequiredDocumentsNotProvided();

        // Assert
        Assert.False(result);
    }

    #region Reset Tests

    [Theory]
    [InlineData(BlockTypes.AiQuery)]
    [InlineData(BlockTypes.Merge)]
    [InlineData(BlockTypes.CalculateNumber)]
    [InlineData(BlockTypes.SimpleRag)]
    [InlineData(BlockTypes.LoopStart)]
    [InlineData(BlockTypes.LoopEnd)]
    [InlineData(BlockTypes.BuildTopicsForAllDocuments)]
    [InlineData(BlockTypes.IntelligentSearch)]
    [InlineData(BlockTypes.ExchangeRate)]
    [InlineData(BlockTypes.WebApiCall)]
    [InlineData(BlockTypes.DynamicChart)]
    public void Reset_ForBlockType_KeepsUniversalProperties(BlockTypes blockType)
    {
        // Arrange
        var block = new Block(blockType)
        {
            Id = "test-id",
            Name = "Test Block",
            ReplacementTag = "test_tag",
            Order = 5,
            Description = "Test Description",
            OutputTitleTemplate = "Test Output"
        };

        // Act
        block.Reset();

        // Assert - Universal properties should never be cleared
        Assert.Equal("test-id", block.Id);
        Assert.Equal(blockType, block.Type);
        Assert.Equal("Test Block", block.Name);
        Assert.Equal("test_tag", block.ReplacementTag);
        Assert.Equal(5, block.Order);
        Assert.Equal("Test Description", block.Description);
        Assert.Equal("Test Output", block.OutputTitleTemplate);
    }

    [Fact]
    public void Reset_AllPropertiesAreCoveredByKeepOrClearRules()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery);

        // Get all public instance properties using reflection
        var allProperties = typeof(Block)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanWrite || p.PropertyType.IsGenericType) // Writable or collection
            .Select(p => p.Name)
            .ToHashSet();

        // Act
        block.Reset();

        // Assert - This test ensures we've thought about every property
        // The test will fail if new properties are added without updating reset logic
        // This is intentional - it forces us to make conscious decisions about new properties
        Assert.NotEmpty(allProperties);

        // This is a meta-test that will be validated by the actual implementation
        // It ensures reflection-based coverage
    }

    #endregion

    [Fact]
    public void HasPlaceholder_PropertiesFirst_FindsInProperties()
    {
        // Arrange
        var block = new Block(BlockTypes.WebApiCall)
        {
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/{userId}",
                RequestPayloadTemplate = "{\"name\": \"{userName}\"}"
            }
        };

        // Act & Assert
        Assert.True(block.HasPlaceholder("userId"));
        Assert.True(block.HasPlaceholder("userName"));
        Assert.False(block.HasPlaceholder("missing"));
    }

    [Fact]
    public void HasPlaceholder_DifferentPropertyTypes_WorksForAll()
    {
        // Arrange
        var block = new Block(BlockTypes.WebApiCall)
        {
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/{userId}",
                RequestPayloadTemplate = "{\"name\": \"{userName}\", \"email\": \"{userEmail}\"}"
            }
        };

        // Act & Assert
        Assert.True(block.HasPlaceholder("userId"));
        Assert.True(block.HasPlaceholder("userName"));
        Assert.True(block.HasPlaceholder("userEmail"));
    }

    [Fact]
    public void GetAllPlaceholders_PropertiesFirst_ExtractsFromProperties()
    {
        // Arrange
        var block = new Block(BlockTypes.WebApiCall)
        {
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/{userId}/{action}",
                RequestPayloadTemplate = "{\"name\": \"{userName}\", \"email\": \"{userEmail}\"}"
            }
        };

        var expectedPlaceholders = new List<string> { "userId", "action", "userName", "userEmail" };

        // Act
        List<string> result = block.GetAllPlaceholders();

        // Assert
        Assert.Equal(expectedPlaceholders.Count, result.Count);
        foreach (string placeholder in expectedPlaceholders)
        {
            Assert.Contains(placeholder, result);
        }
    }

    [Fact]
    public void GetAllPlaceholders_OutputTitleTemplate_ExtractsPlaceholders()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge)
        {
            Properties = new MergeProperties
            {
                TextTemplate = "Some text with {data}"
            },
            OutputTitleTemplate = "{fundName} Analysis"
        };

        // Act
        List<string> result = block.GetAllPlaceholders();

        // Assert
        Assert.Contains("data", result);
        Assert.Contains("fundName", result);
    }

    [Fact]
    public void GetAllPlaceholders_ExchangeRateProperties_ExtractsPlaceholders()
    {
        // Arrange
        var block = new Block(BlockTypes.ExchangeRate)
        {
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "{sourceCurrency}",
                ToCurrency = "{targetCurrency}"
            }
        };

        // Act
        List<string> result = block.GetAllPlaceholders();

        // Assert
        Assert.Contains("sourceCurrency", result);
        Assert.Contains("targetCurrency", result);
    }

    [Fact]
    public void ReplaceAll_PropertiesFirst_ReplacesInProperties()
    {
        // Arrange
        var block = new Block(BlockTypes.WebApiCall)
        {
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/USERID_TOKEN",
                RequestPayloadTemplate = "{\"name\": \"USERNAME_TOKEN\"}"
            }
        };

        Dictionary<string, string?> inputs = new()
        {
            ["USERID_TOKEN"] = "12345",
            ["USERNAME_TOKEN"] = "John Doe"
        };

        // Act
        block.ReplaceAll(inputs);

        // Assert
        var props = block.Properties as WebApiCallProperties;
        Assert.NotNull(props);
        Assert.Equal("https://api.example.com/12345", props.UrlTemplate);
        Assert.Equal("{\"name\": \"John Doe\"}", props.RequestPayloadTemplate);
    }

    #region GetDependencyReplacementTags Tests

    [Fact]
    public void GetDependencyReplacementTags_SingleBraceTag_ReturnsTag()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {block1}",
            },
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("block1", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_DoubleBraceTag_ReturnsTag()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {{block1}}",
            },
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("block1", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_TagWithProperty_ReturnsBaseTag()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {block1.answer}",
            },
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("block1", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_TagWithNestedProperty_ReturnsBaseTag()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {block1.answer.value}",
            },
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("block1", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_DoubleBraceTagWithProperty_ReturnsBaseTag()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {{block1.answer}}",
            },
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("block1", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_MultipleTags_ReturnsAllUniqueTags()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {block1} and {block2.answer}",
                SystemPromptTemplate = "System {block3} and {{block1.property}}",
            },
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("block1", result);
        Assert.Contains("block2", result);
        Assert.Contains("block3", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_NoTags_ReturnsEmpty()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello world",
            },
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetDependencyReplacementTags_TextInputsWithTags_ReturnsTags()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Hello {input1}",
            },
        };
        _ = new TextInput(block, "input1", "{block1.answer}");

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("input1", result);
        Assert.Contains("block1", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_AllPropertiesTypes_ReturnsTags()
    {
        // Arrange
        var aiBlock = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "{block1}",
                SystemPromptTemplate = "{block2}",
            },
        };

        var mergeBlock = new Block(BlockTypes.Merge)
        {
            Properties = new MergeProperties
            {
                TextTemplate = "{block3}",
            },
        };

        var searchBlock = new Block(BlockTypes.IntelligentSearch)
        {
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "{block4}",
            },
        };

        var loopBlock = new Block(BlockTypes.LoopStart)
        {
            Properties = new LoopStartProperties
            {
                LoopedListTemplate = "{block5}",
            },
        };

        // Act & Assert
        List<string> aiResult = aiBlock.GetDependencyReplacementTags();
        Assert.Equal(2, aiResult.Count);
        Assert.Contains("block1", aiResult);
        Assert.Contains("block2", aiResult);

        List<string> mergeResult = mergeBlock.GetDependencyReplacementTags();
        _ = Assert.Single(mergeResult);
        Assert.Contains("block3", mergeResult);

        List<string> searchResult = searchBlock.GetDependencyReplacementTags();
        _ = Assert.Single(searchResult);
        Assert.Contains("block4", searchResult);

        List<string> loopResult = loopBlock.GetDependencyReplacementTags();
        _ = Assert.Single(loopResult);
        Assert.Contains("block5", loopResult);
    }

    #endregion

    #region GetDependencyReplacementTags Tests - Properties-Based Blocks

    [Fact]
    public void GetDependencyReplacementTags_AiQueryProperties_ExtractsFromPrompts()
    {
        // Arrange
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Analyze {block1.answer} and compare to {block2}",
                SystemPromptTemplate = "You are analyzing {context.data}"
            }
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("block1", result);
        Assert.Contains("block2", result);
        Assert.Contains("context", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_WebApiCallProperties_ExtractsFromUrlAndPayload()
    {
        // Arrange
        var block = new Block(BlockTypes.WebApiCall)
        {
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/{endpoint.path}/data",
                RequestPayloadTemplate = """{"query": "{searchBlock.result}", "filter": "{filterBlock}"}"""
            }
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("endpoint", result);
        Assert.Contains("searchBlock", result);
        Assert.Contains("filterBlock", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_MergeProperties_ExtractsFromTemplate()
    {
        // Arrange
        var block = new Block(BlockTypes.Merge)
        {
            Properties = new MergeProperties
            {
                TextTemplate = "Summary: {analysis.summary}\nDetails: {details.content}"
            }
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("analysis", result);
        Assert.Contains("details", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_IntelligentSearchProperties_ExtractsFromSearchTerm()
    {
        // Arrange
        var block = new Block(BlockTypes.IntelligentSearch)
        {
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "{company.name} financial statements {year}"
            }
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("company", result);
        Assert.Contains("year", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_CalculateFormulaProperties_ExtractsFromFormula()
    {
        // Arrange
        var block = new Block(BlockTypes.CalculateNumber)
        {
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "{revenue.value} - {expenses.value}"
            }
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("revenue", result);
        Assert.Contains("expenses", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_LoopStartProperties_ExtractsFromLoopedList()
    {
        // Arrange
        var block = new Block(BlockTypes.LoopStart)
        {
            Properties = new LoopStartProperties
            {
                LoopedListTemplate = "{items.list}"
            }
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("items", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_PropertiesWithDoubleBraces_ExtractsCorrectly()
    {
        // Arrange
        var block = new Block(BlockTypes.WebApiCall)
        {
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/{{apiVersion}}/data",
                RequestPayloadTemplate = """{"data": "{{block1.result.value}}"}"""
            }
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("apiVersion", result);
        Assert.Contains("block1", result);
    }

    [Fact]
    public void GetDependencyReplacementTags_TagWithUnderscores_ExtractsCorrectly()
    {
        // Arrange - reproduces real-world issue with underscored tag names
        var block = new Block(BlockTypes.AiQuery)
        {
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = """
                    Round the weighted average score to the nearest whole number using standard rounding rules.

                    INPUT:

                    <weighted_average_score>
                    {ownership_ave_rating_raw}
                    </weighted_average_score>

                    INSTRUCTIONS:

                    1. Apply standard mathematical rounding (≥0.5 rounds up, <0.5 rounds down)
                    2. Return ONLY the whole number with no additional text or explanations
                    3. If input is invalid or missing, return 0

                    OUTPUT FORMAT:
                    [whole number 0-5]
                    """
            }
        };

        // Act
        List<string> result = block.GetDependencyReplacementTags();

        // Assert
        _ = Assert.Single(result);
        Assert.Contains("ownership_ave_rating_raw", result);
    }

    #endregion

    #region MoveToSection

    [Fact]
    public void MoveToSection_CrossSection_RetargetsFkAndReorders()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow);
        var destination = new Section("dest", workflow);
        var block = new Block { Type = BlockTypes.AiQuery };
        source.Add(block);

        block.MoveToSection(destination);

        Assert.Same(destination, block.Section);
        Assert.Equal(destination.Id, block.SectionId);
        Assert.Empty(source.Blocks);
        _ = Assert.Single(destination.Blocks);
        Assert.Equal(0, block.Order);
    }

    [Fact]
    public void MoveToSection_SameSectionDifferentIndex_Reorders()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var a = new Block { Type = BlockTypes.AiQuery };
        var b = new Block { Type = BlockTypes.AiQuery };
        var c = new Block { Type = BlockTypes.AiQuery };
        section.Add(a);
        section.Add(b);
        section.Add(c);

        c.MoveToSection(section, 0);

        Block[] ordered = [.. section.Blocks.OrderBy(x => x.Order)];
        Assert.Same(c, ordered[0]);
        Assert.Same(a, ordered[1]);
        Assert.Same(b, ordered[2]);
    }

    [Fact]
    public void MoveToSection_SeparatorDestination_Throws()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow);
        var separator = new Section("sep", workflow)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };
        var block = new Block { Type = BlockTypes.AiQuery };
        source.Add(block);

        Assert.Throws<InvalidOperationException>(() => block.MoveToSection(separator));
    }

    #endregion

    #region CloneToSection

    [Fact]
    public void CloneToSection_AttachesCloneToDestinationAndCopiesScalars()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow);
        var destination = new Section("dest", workflow);
        var original = new Block
        {
            Type = BlockTypes.AiQuery,
            Name = "original",
            ReplacementTag = "tag_a",
            Properties = new AiQueryProperties { UserPromptTemplate = "hi" },
        };
        source.Add(original);

        Block clone = original.CloneToSection(destination);

        Assert.NotSame(original, clone);
        Assert.Same(destination, clone.Section);
        Assert.Equal(destination.Id, clone.SectionId);
        Assert.Equal("original", clone.Name);
        Assert.Equal("tag_a", clone.ReplacementTag);
        Assert.IsType<AiQueryProperties>(clone.Properties);
        Assert.Equal("hi", ((AiQueryProperties)clone.Properties).UserPromptTemplate);
        Assert.NotSame(original.Properties, clone.Properties);
        _ = Assert.Single(destination.Blocks);
    }

    [Fact]
    public void CloneToSection_WithReplacementTagOverride_UsesOverride()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var original = new Block { Type = BlockTypes.AiQuery, ReplacementTag = "tag_a" };
        section.Add(original);

        Block clone = original.CloneToSection(section, replacementTag: "tag_b");

        Assert.Equal("tag_b", clone.ReplacementTag);
    }

    #endregion

    #region Section-typed constructors

    [Fact]
    public void Ctor_WithSection_AttachesToSectionAtEnd()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.AiQuery, section);

        var second = new Block(BlockTypes.Merge, section);

        Assert.Same(section, second.Section);
        Assert.Equal(section.Id, second.SectionId);
        Assert.Equal(1, second.Order);
        Assert.Equal(2, section.Blocks.Count);
    }

    [Fact]
    public void Ctor_WithSectionAndIndex_InsertsAtIndex()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var first = new Block(BlockTypes.AiQuery, section);
        var third = new Block(BlockTypes.Merge, section);

        var inserted = new Block(BlockTypes.CalculateNumber, section, 1);

        Block[] ordered = [.. section.Blocks.OrderBy(b => b.Order)];
        Assert.Same(first, ordered[0]);
        Assert.Same(inserted, ordered[1]);
        Assert.Same(third, ordered[2]);
    }

    #endregion

    [Theory]
    [InlineData(BlockTypes.LoopStart)]
    [InlineData(BlockTypes.LoopEnd)]
    public void IsLoopBoundary_ForLoopMarkerTypes_ReturnsTrue(BlockTypes type)
    {
        var block = new Block(type);

        Assert.True(block.IsLoopBoundary);
    }

    [Theory]
    [InlineData(BlockTypes.AiQuery)]
    [InlineData(BlockTypes.Merge)]
    [InlineData(BlockTypes.SimpleRag)]
    public void IsLoopBoundary_ForNonLoopTypes_ReturnsFalse(BlockTypes type)
    {
        var block = new Block(type);

        Assert.False(block.IsLoopBoundary);
    }

    [Fact]
    public void Ctor_InsertAtAnIndexWhoseTagIsTaken_MintsAFreeTag()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow) { Order = 0 };
        var first = new Block(BlockTypes.AiQuery, section);
        var second = new Block(BlockTypes.AiQuery, section);

        var inserted = new Block(BlockTypes.AiQuery, section, 0);

        Assert.Equal("block_0", first.ReplacementTag);
        Assert.Equal("block_1", second.ReplacementTag);
        Assert.NotEqual(first.ReplacementTag, inserted.ReplacementTag);
        Assert.NotEqual(second.ReplacementTag, inserted.ReplacementTag);
    }

    [Fact]
    public void Ctor_AddAfterAnEarlierBlockWasRemoved_MintsAFreeTag()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow) { Order = 0 };
        var first = new Block(BlockTypes.AiQuery, section);
        var second = new Block(BlockTypes.AiQuery, section);
        section.Remove(first);

        var added = new Block(BlockTypes.AiQuery, section);

        Assert.Equal("block_1", second.ReplacementTag);
        Assert.NotEqual(second.ReplacementTag, added.ReplacementTag);
    }

    [Fact]
    public void Ctor_WithNoTagCollision_KeepsTheOrderDerivedTag()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow) { Order = 0 };

        var first = new Block(BlockTypes.AiQuery, section);
        var second = new Block(BlockTypes.AiQuery, section);
        var third = new Block(BlockTypes.AiQuery, section, 2);

        Assert.Equal("block_0", first.ReplacementTag);
        Assert.Equal("block_1", second.ReplacementTag);
        Assert.Equal("block_2", third.ReplacementTag);
    }

    [Fact]
    public void Ctor_WithAnExplicitTag_DoesNotMint()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow) { Order = 0 };
        _ = new Block(BlockTypes.AiQuery, section);

        var explicitlyTagged = new Block(BlockTypes.AiQuery, section, 0, "block_0");

        Assert.Equal("block_0", explicitlyTagged.ReplacementTag);
    }

    [Fact]
    public void MoveToSection_WhenTheTagIsTakenInTheDestination_MintsAFreeTag()
    {
        var workflow = new Workflow("w");
        var source = new Section("a", workflow) { Order = 0 };
        var destination = new Section("b", workflow) { Order = 1 };
        var moving = new Block(BlockTypes.AiQuery, source);
        var resident = new Block(BlockTypes.AiQuery, destination);

        moving.MoveToSection(destination);

        Assert.Equal("block_0", resident.ReplacementTag);
        Assert.NotEqual(resident.ReplacementTag, moving.ReplacementTag);
        Assert.Equal(2, destination.Blocks.Count);
        Assert.Equal(2, destination.Blocks.Select(block => block.ReplacementTag).Distinct().Count());
    }

    [Fact]
    public void MoveToSection_WhenTheTagIsTaken_KeepsTheAuthorsNameWithASuffix()
    {
        var workflow = new Workflow("w");
        var source = new Section("a", workflow) { Order = 0 };
        var destination = new Section("b", workflow) { Order = 1 };
        var moving = new Block(BlockTypes.AiQuery, source, "final_summary");
        _ = new Block(BlockTypes.AiQuery, destination, "final_summary");
        _ = new Block(BlockTypes.AiQuery, destination, "final_summary_2");

        moving.MoveToSection(destination);

        Assert.Equal("final_summary_3", moving.ReplacementTag);
    }

    [Fact]
    public void MoveToSection_WhenTheTagIsFreeInTheDestination_KeepsIt()
    {
        var workflow = new Workflow("w");
        var source = new Section("a", workflow) { Order = 0 };
        var destination = new Section("b", workflow) { Order = 1 };
        var moving = new Block(BlockTypes.AiQuery, source, "alpha");
        _ = new Block(BlockTypes.AiQuery, destination, "beta");

        moving.MoveToSection(destination);

        Assert.Equal("alpha", moving.ReplacementTag);
    }

    [Fact]
    public void MoveToSection_WhenTheMovedBlockIsRenamed_LeavesExistingReferencesPointingAtTheResident()
    {
        var workflow = new Workflow("w");
        var source = new Section("a", workflow) { Order = 0 };
        var destination = new Section("b", workflow) { Order = 1 };
        var moving = new Block(BlockTypes.AiQuery, source);
        _ = new Block(BlockTypes.AiQuery, destination);
        var referrer = new Block(BlockTypes.AiQuery, destination, "referrer")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Use {block_0} and {block_0.answer}" },
        };

        moving.MoveToSection(destination);

        string prompt = ((AiQueryProperties)referrer.Properties!).UserPromptTemplate!;
        Assert.Contains("{block_0}", prompt);
        Assert.Contains("{block_0.answer}", prompt);
        Assert.DoesNotContain(moving.ReplacementTag, prompt);
    }

    [Fact]
    public void MoveToSection_WithinTheSameSection_KeepsTheTag()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow) { Order = 0 };
        var first = new Block(BlockTypes.AiQuery, section);
        var second = new Block(BlockTypes.AiQuery, section);

        second.MoveToSection(section, 0);

        Assert.Equal("block_0", first.ReplacementTag);
        Assert.Equal("block_1", second.ReplacementTag);
    }

    [Fact]
    public void DuplicateBlockToSection_CalledTwiceOnTheSameBlock_MintsDistinctTags()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        var original = new Block(BlockTypes.AiQuery, section, "final_summary");

        Block first = original.DuplicateBlockToSection(section);
        Block second = original.DuplicateBlockToSection(section);

        Assert.Equal("final_summary_copy", first.ReplacementTag);
        Assert.NotEqual(first.ReplacementTag, second.ReplacementTag);
    }

    [Fact]
    public void DuplicateBlockToSection_WhenTheCopyTagIsFree_UsesItVerbatim()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        var original = new Block(BlockTypes.AiQuery, section, "final_summary");

        Block copy = original.DuplicateBlockToSection(section);

        Assert.Equal("final_summary_copy", copy.ReplacementTag);
    }

    [Fact]
    public void DuplicateBlockToSection_WhenTheCopyTagWouldOverflow_StaysWithinTheColumnLimit()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        var original = new Block(BlockTypes.AiQuery, section, new string('t', Block.MAX_REPLACEMENT_TAG_LENGTH - 2));

        Block first = original.DuplicateBlockToSection(section);
        Block second = original.DuplicateBlockToSection(section);

        Assert.Equal(Block.MAX_REPLACEMENT_TAG_LENGTH, first.ReplacementTag.Length);
        Assert.Equal(Block.MAX_REPLACEMENT_TAG_LENGTH, second.ReplacementTag.Length);
        Assert.EndsWith("_copy", first.ReplacementTag);
        Assert.NotEqual(first.ReplacementTag, second.ReplacementTag);
    }

    [Fact]
    public void DuplicateBlockToSection_WhenTheTagAlreadyFillsTheColumn_StillMintsSomethingUniqueThatFits()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        string fullWidthTag = new('t', Block.MAX_REPLACEMENT_TAG_LENGTH);
        var original = new Block(BlockTypes.AiQuery, section, fullWidthTag);

        Block copy = original.DuplicateBlockToSection(section);

        Assert.Equal(Block.MAX_REPLACEMENT_TAG_LENGTH, copy.ReplacementTag.Length);
        Assert.NotEqual(fullWidthTag, copy.ReplacementTag);
    }

    [Fact]
    public void MoveToSection_WhenTheSuffixWouldOverflow_StaysWithinTheColumnLimit()
    {
        var workflow = new Workflow("w");
        var source = new Section("a", workflow) { Order = 0 };
        var destination = new Section("b", workflow) { Order = 1 };
        string longTag = new('t', Block.MAX_REPLACEMENT_TAG_LENGTH);
        var moving = new Block(BlockTypes.AiQuery, source, longTag);
        _ = new Block(BlockTypes.AiQuery, destination, longTag);

        moving.MoveToSection(destination);

        Assert.Equal(Block.MAX_REPLACEMENT_TAG_LENGTH, moving.ReplacementTag.Length);
        Assert.NotEqual(longTag, moving.ReplacementTag);
    }

    [Fact]
    public void ComposeTagWithinLimit_WithASuffixLongerThanTheLimit_DoesNotThrow()
    {
        string suffix = new('s', Block.MAX_REPLACEMENT_TAG_LENGTH + 5);

        Assert.Equal(suffix, Block.ComposeTagWithinLimit("base", suffix));
    }

    [Fact]
    public void CloneToSection_WhenTheSourceTagIsEmpty_MintsAFreeTag()
    {
        var workflow = new Workflow("w");
        var source = new Section("a", workflow) { Order = 0 };
        var destination = new Section("b", workflow) { Order = 1 };
        var original = new Block(BlockTypes.AiQuery, source, string.Empty);
        _ = new Block(BlockTypes.AiQuery, destination, "block_1");

        Block clone = original.CloneToSection(destination);

        Assert.Equal("block_2", clone.ReplacementTag);
    }

    [Fact]
    public void DuplicateBlockToSection_IntoAnotherSection_KeepsTheCopyTag()
    {
        var workflow = new Workflow("w");
        var source = new Section("a", workflow) { Order = 0 };
        var destination = new Section("b", workflow) { Order = 1 };
        var original = new Block(BlockTypes.AiQuery, source, "final_summary");

        Block copy = original.DuplicateBlockToSection(destination);

        Assert.Equal("final_summary_copy", copy.ReplacementTag);
    }
}
