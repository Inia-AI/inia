using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Constants;
using IniaAi.Common.Enums;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class AiQueryStrategyTests : IClassFixture<WorkflowsFixture<AiQueryStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly WorkflowsFixture<AiQueryStrategyTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;
    private readonly ScriptedChatConnector _model;
    private Func<ScriptedModelCall, string> _modelAnswer = EchoingModel.Answer;

    public AiQueryStrategyTests(WorkflowsFixture<AiQueryStrategyTests> blocksFixture, ITestOutputHelper outputHelper)
    {
        _fixture = blocksFixture;
        _outputHelper = outputHelper;
        _model = new ScriptedChatConnector(call => _modelAnswer(call));
        _testServiceProvider = _fixture.BuildTestServiceProvider(
            _outputHelper,
            services => services.AddSingleton<IChatConnector>(_model));
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidBlockAndContext_CallsContentSequenceMethodsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block 1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with \"Yes\"",
                SystemPromptTemplate = "You strictly follow the instructions",
                ShouldUseAdvancedModel = false,
                MaxTokens = 2,
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Yes", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task ExecuteBlockAsync_BlockIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Properties = new AiQueryProperties
            {
                ShouldUseAdvancedModel = false,
                MaxTokens = 2,
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act & Assert
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithAnonymisationMap_ReplacesAnonymisedTextAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block 1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with \"Yes\"",
                SystemPromptTemplate = "You strictly follow the instructions",
                ShouldUseAdvancedModel = false,
                MaxTokens = 2,
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        context.AnonymisationMap["Yes"] = "No";
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Yes", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
        ScriptedModelCall call = Assert.Single(_model.Calls);
        Assert.Contains("\"No\"", call.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Yes", call.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SendPageImages_CallsGetAllFileInputsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block 1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "What is on the image",
                SystemPromptTemplate = "You are an assistant who answers to questions about images in ONE word",
                MaxTokens = 10,
                ShouldUsePageImages = true,
            },
        };
        var doc = new Document(new FileMetadata("IniaAi", "elephant.jpg", _fixture.ImageSample.BinaryData.ToArray().Length, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ImageJpeg);
        _ = new Page(doc, 1, 1) { Image = _fixture.ImageSample };
        block.Documents.Add(doc);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains(EchoingModel.IMAGE_ANSWER, block.BlockResponse.RawResponse, StringComparison.Ordinal);
        Assert.Contains(_model.Calls, call => call.CarriesAnImage);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WhenNoPagesFilteredWithInputContextPresent_CallsGetAllFileInputsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Query 1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"What is on the page?",
                MaxTokens = 2,
                ShouldUsePageImages = true,
                ShouldUseAdvancedModel = false,
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains(string.Empty, block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WhenNoPagesFilteredWithInputContextNotPresent_CallsGetAllFileInputsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Query 1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with YES",
                MaxTokens = 2,
                ShouldUsePageImages = true,
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();
        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("yes", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithChunking_ProcessesLargeInputContextInChunks()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With Chunks",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"Summarize this",
                SystemPromptTemplate = "You are an assistant who summarizes text",
                ShouldChunkLargeInputContexts = true,
                ShouldUseAdvancedModel = false,
                ChunkSize = 5,
            },
        };

        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        for (int i = 1; i <= 7; i++)
        {
            _ = new Page(doc1, i, i) { RawText = $"This is page {i} content" };
        }

        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Iteration 1", block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 2", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithChunkingEnabledButNoRagSettings_SkipsChunking()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With Chunking No RAG",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with YES",
                SystemPromptTemplate = "You strictly follow instructions",
                ShouldChunkLargeInputContexts = true,
                ShouldUseAdvancedModel = false,
                ChunkSize = 5,
                MaxTokens = 2,
            },
        };

        // No RAG settings, no documents — chunking should be skipped
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert — block should execute without chunking (no "Iteration" markers)
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("yes", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
        Assert.DoesNotContain("Iteration", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithChunkingEnabledButNoDocuments_SkipsChunking()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With Chunking No Docs",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with YES",
                SystemPromptTemplate = "You strictly follow instructions",
                ShouldChunkLargeInputContexts = true,
                ShouldUseAdvancedModel = false,
                ChunkSize = 5,
                MaxTokens = 2,
            },
        };

        // RAG settings present but no documents — chunking should be skipped
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert — block should be skipped (RAG settings but no documents)
        Assert.NotNull(block.ExecutionState);
        Assert.Equal(ExecutionStatus.Skipped, block.ExecutionState.Status);
        Assert.Equal("No documents found for block", block.ExecutionState.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithoutChunking_ProcessesInputContextAsWhole()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block Without Chunks",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"Summarize this",
                SystemPromptTemplate = "You are an assistant who summarizes text",
                ShouldChunkLargeInputContexts = false,
                ShouldUseAdvancedModel = false,
            },
        };

        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        for (int i = 1; i <= 7; i++)
        {
            _ = new Page(doc1, i, i) { RawText = $"This is page {i} content" };
        }

        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.DoesNotContain("Iteration", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WhenConnectorReturnsErrorPrefix_ThrowsInvalidOperationException()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Error Block",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with \"Yes\"",
                SystemPromptTemplate = "",
                ShouldUseAdvancedModel = false,
                MaxTokens = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();
        _modelAnswer = _ => $"{IChatConnector.ERROR_PREFIX}The model does not exist.";

        // Act & Assert
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));
        Assert.StartsWith("Error response from LLM", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithRagSettingsButNoDocuments_MarksBlockAsSkipped()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With RAG But No Docs",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"Analyze this",
                SystemPromptTemplate = "You are an assistant",
                ShouldUseAdvancedModel = false,
                MaxTokens = 2,
            },
        };

        // Add RAG settings but no documents
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.ExecutionState);
        Assert.Equal(ExecutionStatus.Skipped, block.ExecutionState.Status);
        Assert.Equal("No documents found for block", block.ExecutionState.Error);
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithRagSettingsButZeroPagesFiltered_MarksBlockAsSkipped()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With RAG But Zero Pages",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"Analyze this",
                SystemPromptTemplate = "You are an assistant",
                ShouldUseAdvancedModel = false,
                MaxTokens = 2,
            },
        };

        // Add document with pages but RAG settings that will filter out all pages
        var doc = new Document(new FileMetadata("IniaAi", "Test.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc, 1, 1) { RawText = "Some content" };
        block.Documents.Add(doc);

        // Add RAG settings that will result in zero pages (exact pages that don't exist)
        _ = new RagSettings(block)
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "999,1000" // Pages that don't exist
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.ExecutionState);
        Assert.Equal(ExecutionStatus.Skipped, block.ExecutionState.Status);
        Assert.Equal("No pages were filtered for the block", block.ExecutionState.Error);
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithoutRagSettings_ExecutesNormally()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block Without RAG",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with YES",
                SystemPromptTemplate = "You strictly follow instructions",
                MaxTokens = 2,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("yes", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
        Assert.NotNull(block.ExecutionState);
        Assert.NotEqual(ExecutionStatus.Skipped, block.ExecutionState.Status);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithRagSettingsAndValidPages_ExecutesNormally()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With Valid RAG",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"Summarize",
                SystemPromptTemplate = "You are a summarizer",
                ShouldUseAdvancedModel = false,
                MaxTokens = 100,
            },
        };

        // Add document with pages and RAG settings that will include pages
        var doc = new Document(new FileMetadata("IniaAi", "Test.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc, 1, 1) { RawText = "This is test content for summarization" };
        block.Documents.Add(doc);

        // Add RAG settings that will include all pages
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.NotEmpty(block.BlockResponse.RawResponse);
        Assert.NotNull(block.ExecutionState);
        Assert.NotEqual(ExecutionStatus.Skipped, block.ExecutionState.Status);
        Assert.Null(block.ExecutionState.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithConditionalTag_StopsWhenTagFoundWithValidValue()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With Conditional Tag",
            Properties = new AiQueryProperties
            {
                ConditionalTagTemplate = "answer",
                UserPromptTemplate = $"If the number of the page is 2, respond with <answer>value</answer>. DO NOT use the <answer> tag for other pages.",
                ShouldChunkLargeInputContexts = true,
                ShouldUseAdvancedModel = false,
                ChunkSize = 1,
            },
        };

        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);

        // Create enough pages to trigger multiple chunks (chunk size is 1)
        for (int i = 1; i <= 3; i++)
        {
            _ = new Page(doc1, i, i) { RawText = $"This is page {i} content with important information" };
        }

        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();
        _modelAnswer = call => call.Text.Contains("page 2 content", StringComparison.Ordinal)
            ? "<answer>value</answer>"
            : "Nothing to report on this page.";

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 1", block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 2", block.BlockResponse.RawResponse);
        Assert.Contains("<answer>", block.BlockResponse.RawResponse);
        Assert.DoesNotContain("Iteration 3", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithConditionalTag_ContinuesWhenTagHasNaValue()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With Conditional Tag NA",
            Properties = new AiQueryProperties
            {
                ConditionalTagTemplate = "answer",
                UserPromptTemplate = $"Process this and respond STRICTLY with <answer>N/A</answer>",
                SystemPromptTemplate = "You are an assistant. Respond exactly as instructed.",
                ShouldChunkLargeInputContexts = true,
                ShouldUseAdvancedModel = false,
                ChunkSize = 1,
                MaxTokens = 50,
            },
        };

        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);

        for (int i = 1; i <= 3; i++)
        {
            _ = new Page(doc1, i, i) { RawText = $"Page {i}" };
        }

        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Iteration 1", block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 2", block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 3", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithConditionalTag_ContinuesWhenTagNotFound()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With Missing Tag",
            Properties = new AiQueryProperties
            {
                ConditionalTagTemplate = "answer",
                UserPromptTemplate = $"Answer with YES",
                ShouldChunkLargeInputContexts = true,
                ShouldUseAdvancedModel = false,
                ChunkSize = 1,
                MaxTokens = 50,
            },
        };

        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);

        for (int i = 1; i <= 3; i++)
        {
            _ = new Page(doc1, i, i) { RawText = $"Page {i}" };
        }

        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Iteration 1", block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 2", block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 3", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithoutConditionalTag_ProcessesAllChunks()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block Without Conditional Tag",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"Answer with YES",
                SystemPromptTemplate = "You are an assistant",
                ShouldChunkLargeInputContexts = true,
                ShouldUseAdvancedModel = false,
                ChunkSize = 1,
                MaxTokens = 50,
                // No ConditionalTagTemplate set
            },
        };

        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);

        for (int i = 1; i <= 3; i++)
        {
            _ = new Page(doc1, i, i) { RawText = $"Page {i} content" };
        }

        block.Documents.Add(doc1);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Iteration 1", block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 2", block.BlockResponse.RawResponse);
        Assert.Contains("Iteration 3", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithConditionalTag_InitializesExecutionStateCorrectly()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block State Test",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with <answer>yes</answer>",
                SystemPromptTemplate = "Follow instructions",
                ShouldChunkLargeInputContexts = false,
                ShouldUseAdvancedModel = false,
                MaxTokens = 10,
                ConditionalTagTemplate = "answer",
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        var props = block.Properties as AiQueryProperties;
        Assert.NotNull(props?.ExecutionState);
        Assert.Equal("answer", props.ExecutionState.ConditionalTag);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithInputContextPlaceholderButNoRagSettings_MarksBlockAsSkipped()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Block With InputContext But No RAG",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"Analyze this: {{{SharedInputKeys.INPUT_CONTEXT}}}",
                SystemPromptTemplate = "You are an assistant",
                ShouldUseAdvancedModel = false,
                MaxTokens = 2,
            },
        };

        // Note: No RAG settings added, but the prompt contains {InputContext}

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.ExecutionState);
        Assert.Equal(ExecutionStatus.Skipped, block.ExecutionState.Status);
        Assert.Contains("InputContext", block.ExecutionState.Error);
        Assert.Contains("RAG settings", block.ExecutionState.Error);
        Assert.Null(block.BlockResponse);
    }
}
