using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Enums;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Constants;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Document = IniaAi.Domain.Entities.Documents.Document;

namespace IniaAi.Core.Tests.Workflows;

public class WorkflowHandlerTests : IClassFixture<WorkflowsFixture<WorkflowHandler>>
{
    private const string ID_CONTAINING_STOP_TERM = "ff99747a-eb9b-40eb-af11-be19f117a74c";

    private readonly WorkflowsFixture<WorkflowHandler> _fixture;
    private readonly WorkflowHandler _workflowHandler;
    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public WorkflowHandlerTests(
        WorkflowsFixture<WorkflowHandler> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testServiceProvider = _fixture.BuildTestServiceProvider(
            testOutputHelper,
            services => services
                .AddSingleton<IChatConnector>(new ScriptedChatConnector(EchoingModel.Answer))
                .AddSingleton<IEmbeddingsConnector>(new WordHashEmbeddingsConnector()));
        _workflowHandler = _testServiceProvider.GetRequiredService<WorkflowHandler>();
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ValidWorkflow_GetsResponsesToAllQueriesAsync()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block1 = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block 1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with \"{Answer}\"",
                SystemPromptTemplate = "You strictly follow the instructions",
                MaxTokens = 10,
                ShouldUsePageImages = true,
            },
        };
        _ = new TextInput(block1, "Answer", "Yes");

        var block2 = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block 2",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "{Image}\nWhat is on the image",
                SystemPromptTemplate = "You are an assistant who answers to questions about images in ONE word",
                MaxTokens = 10,
                ShouldUsePageImages = true,
            },
        };
        var fileMetadata = new FileMetadata("IniaAi", "elephant.jpg", _fixture.ImageSample.BinaryData.ToArray().Length, StorageProvider.Database, Guid.NewGuid().ToString());
        var doc = new Document(fileMetadata, MediaType.ImageJpeg);
        var page = new Page(doc, 1, 1) { Image = _fixture.ImageSample };
        block2.Documents.Add(doc);
        _ = new RagSettings(block2) { Type = RagTypes.WholeDocument };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(section.Blocks.All(block => block.BlockResponse is not null));
        Assert.Contains("yes", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EchoingModel.IMAGE_ANSWER, block2.BlockResponse?.RawResponse, StringComparison.Ordinal);
        Assert.Contains("yes", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EchoingModel.IMAGE_ANSWER, block2.BlockResponse?.RawResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ValidWorkflowWithRagSettings_GetsResponsesToAllQueriesAsync()
    {
        // Arrange
        var fileMetadata1 = new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString());
        var doc1 = new Document(fileMetadata1, MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "This is a page about cars" };
        _ = new Page(doc1, 2, 2) { RawText = "This is a page about planes" };
        _ = new Page(doc1, 3, 3) { RawText = "This is a page about ships" };

        var fileMetadata2 = new FileMetadata("IniaAi", "Doc2.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString());
        var doc2 = new Document(fileMetadata2, MediaType.ApplicationPdf);
        _ = new Page(doc2, 1, 1) { RawText = "This is a page about elephants" };
        _ = new Page(doc2, 2, 2) { RawText = "This is a page about lions" };
        _ = new Page(doc2, 3, 3) { RawText = "This is a page about tigers" };
        _ = new Page(doc2, 4, 4) { RawText = "This is a page about bears" };
        _ = new Page(doc2, 5, 5) { RawText = "Rockets rockets rockets rockets rockets rockets" };
        _ = new Page(doc2, 6, 6) { RawText = "This is a page about monkeys" };
        _ = new Page(doc2, 7, 7) { RawText = "This is a page about dogs" };
        _ = new Page(doc2, 8, 8) { RawText = "This is a page about cats" };

        await GetPdfEmbeddingsAsync([doc1, doc2]);

        string userPrompt =
        $$"""
        Reply with a list of names of files and numbers of pages form them.
        For each page - summarize the content of the page in ONE word.

        For the page number use only the number, NOT "pageN".

        DO NOT include extensions in file names.

        Put your answer in <answer> tag.

        Example:

        <answer>
        file1:
        5 - funds
        8 - blank
        9 - number
        </answer>
        """;

        var workflow = new Workflow();
        var section = new Section("", workflow);

        var aiQueryWithNoTypeAnyDoc = new Block(BlockTypes.AiQuery, section)
        {
            Name = "AI Block with N Pages Starting From Index Type",
            ReplacementTag = "aiQueryWithNoTypeAnyDoc",
            SupportedDocumentClasses = ["Any"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = userPrompt,
                MaxTokens = 100,
            },
        };
        aiQueryWithNoTypeAnyDoc.Documents.Add(doc1);
        aiQueryWithNoTypeAnyDoc.Documents.Add(doc2);
        _ = new RagSettings(aiQueryWithNoTypeAnyDoc) { Type = RagTypes.WholeDocument };

        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Apply template",
            ReplacementTag = "applyTemplateQuery1",
            Properties = new MergeProperties
            {
                TextTemplate = "This is a template. Previous block said:\n\n{extractFromTagsQuery1}",
            },
        };

        var calculateQuery = new Block(BlockTypes.CalculateNumber, section)
        {
            Name = "Calculate",
            ReplacementTag = "calculateQuery",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "({number1} + {number2}) * {number3} / {number4}",
            },
        };
        _ = new TextInput(calculateQuery, "number1", "5");
        _ = new TextInput(calculateQuery, "number2", "15");
        _ = new TextInput(calculateQuery, "number3", "2");
        _ = new TextInput(calculateQuery, "number4", "40");

        var aiQueryWithNPagesTypeForDoc1 = new Block(BlockTypes.AiQuery, section)
        {
            Name = "AI Block with N Pages Starting From Index Type for Doc1",
            SupportedDocumentClasses = ["document1"],
            ReplacementTag = "aiQueryWithNPagesTypeForDoc1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = userPrompt,
                MaxTokens = 100,
            },
        };
        _ = new RagSettings(aiQueryWithNPagesTypeForDoc1)
        {
            StartIndexInput = "2",
            EndIndexInput = "3",
            NumberOfPagesInput = "{calculateQuery}",
            Type = RagTypes.NPagesStartingFromIndex,
        };
        aiQueryWithNPagesTypeForDoc1.Documents.Add(doc1);

        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "AI Block with No Docs",
            ReplacementTag = "noDocsAiQuery",
            SupportedDocumentClasses = ["None"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate =
                """
                Summarize the text below in TWO words separated with a new line.
                <text>
                {extractFromTagsQuery2}
                </text>

                Example of answer:

                Page
                Numbers
                """,
                MaxTokens = 100,
            },
        };

        var aiQueryWithTopNPagesPerFilterTypeForDoc2 = new Block(BlockTypes.AiQuery, section)
        {
            Name = "AI Block with Top N Pages Per Filter Type for Doc2",
            ReplacementTag = "aiQueryWithTopNPagesPerFilterTypeForDoc2",
            SupportedDocumentClasses = ["document2"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = userPrompt,
                MaxTokens = 100,
            },
        };
        _ = new RagSettings(aiQueryWithTopNPagesPerFilterTypeForDoc2)
        {
            NumberOfPagesInput = "1",
            Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors,
            SmartFilterExpressions = ["rockets"]
        };
        aiQueryWithTopNPagesPerFilterTypeForDoc2.Documents.Add(doc2);

        var aiQueryWithKeyWordType = new Block(BlockTypes.AiQuery, section)
        {
            Name = "AI Block with Keyword Type",
            ReplacementTag = "aiQueryWithKeyWordType",
            SupportedDocumentClasses = ["Any"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = userPrompt,
                MaxTokens = 100,
            },
        };
        _ = new RagSettings(aiQueryWithKeyWordType)
        {
            Type = RagTypes.KeywordBased,
            NumberOfPagesInput = "1",
            Keywords = ["dogs", "ships"],
            SmartFilterExpressions = ["rockets"],
        };
        aiQueryWithKeyWordType.Documents.Add(doc1);
        aiQueryWithKeyWordType.Documents.Add(doc2);

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("1", calculateQuery.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        AssertResponseContainsLines(
            aiQueryWithNoTypeAnyDoc.BlockResponse?.RawResponse,
            "<page1(Doc1.pdf)-text>\nThis is a page about cars",
            "<page2(Doc1.pdf)-text>\nThis is a page about planes",
            "<page3(Doc1.pdf)-text>\nThis is a page about ships",
            "<page4(Doc2.pdf)-text>\nThis is a page about elephants",
            "<page5(Doc2.pdf)-text>\nThis is a page about lions",
            "<page6(Doc2.pdf)-text>\nThis is a page about tigers",
            "<page7(Doc2.pdf)-text>\nThis is a page about bears",
            "<page8(Doc2.pdf)-text>\nRockets rockets",
            "<page9(Doc2.pdf)-text>\nThis is a page about monkeys",
            "<page10(Doc2.pdf)-text>\nThis is a page about dogs",
            "<page11(Doc2.pdf)-text>\nThis is a page about cats");
        AssertResponseContainsLines(
            aiQueryWithNPagesTypeForDoc1.BlockResponse?.RawResponse,
            "<page2(Doc1.pdf)-text>\nThis is a page about planes");
        AssertResponseOmits(aiQueryWithNPagesTypeForDoc1.BlockResponse?.RawResponse, "about cars", "about ships");
        AssertResponseContainsLines(
            aiQueryWithTopNPagesPerFilterTypeForDoc2.BlockResponse?.RawResponse,
            "<page4(Doc2.pdf)-text>\nThis is a page about bears",
            "<page5(Doc2.pdf)-text>\nRockets rockets",
            "<page6(Doc2.pdf)-text>\nThis is a page about monkeys");
        AssertResponseOmits(
            aiQueryWithTopNPagesPerFilterTypeForDoc2.BlockResponse?.RawResponse,
            "about cars", "about planes", "about ships", "about elephants", "about lions", "about tigers", "about dogs", "about cats");
        AssertResponseContainsLines(
            aiQueryWithKeyWordType.BlockResponse?.RawResponse,
            "<page3(Doc1.pdf)-text>\nThis is a page about ships",
            "<page7(Doc2.pdf)-text>\nThis is a page about bears",
            "<page8(Doc2.pdf)-text>\nRockets rockets",
            "<page9(Doc2.pdf)-text>\nThis is a page about monkeys",
            "<page10(Doc2.pdf)-text>\nThis is a page about dogs");
        AssertResponseOmits(
            aiQueryWithKeyWordType.BlockResponse?.RawResponse,
            "about cars", "about planes", "about elephants", "about lions", "about tigers", "about cats");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithComplexNestedWorkflow_ExecutesCorrectlyAsync()
    {
        // Arrange
        var mainWorkflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };

        var section1 = new Section("First Level Workflow 1", mainWorkflow)
        {
            Title = "First Level Workflow 1",
        };

        var section2 = new Section("First Level Workflow 2", mainWorkflow)
        {
            Title = "First Level Workflow 2",
        };

        var section3 = new Section("Second Level Workflow", mainWorkflow)
        {
            Title = "Second Level Workflow",
        };

        var firstLevelBlock1 = new Block(BlockTypes.AiQuery, section1, "firstLevelBlock1")
        {
            Name = "First Level Block 1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with \"{Answer}\"",
                SystemPromptTemplate = "You strictly follow the instructions",
                MaxTokens = 10,
                ShouldUsePageImages = true,
            },
        };
        _ = new TextInput(firstLevelBlock1, "Answer", "Yes");

        var firstLevelBlock2 = new Block(BlockTypes.AiQuery, section1, "firstLevelBlock2")
        {
            Name = "First Level Block 2",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "{Image}\nWhat is on the image",
                SystemPromptTemplate = "You are an assistant who answers to questions about images in ONE word",
                MaxTokens = 10,
                ShouldUsePageImages = true,
            },
        };
        _ = new RagSettings(firstLevelBlock2) { Type = RagTypes.WholeDocument };
        var fileMetadata = new FileMetadata("IniaAi", "elephant.jpg", _fixture.ImageSample.BinaryData.ToArray().Length, StorageProvider.Database, Guid.NewGuid().ToString());
        var doc = new Document(fileMetadata, MediaType.ImageJpeg);
        var page = new Page(doc, 1, 1) { Image = _fixture.ImageSample };
        firstLevelBlock2.Documents.Add(doc);

        var secondLevelBlock = new Block(BlockTypes.Merge, section3, "secondLevelBlock")
        {
            Name = "Second Level Block",
            Properties = new MergeProperties
            {
                TextTemplate = "First Level Block 1: {firstLevelBlock1}\n\nFirst Level Block 2: {firstLevelBlock2}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(mainWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(mainWorkflow.GetAllBlocks().All(block => block.BlockResponse is not null));
        Assert.Contains("yes", firstLevelBlock1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EchoingModel.IMAGE_ANSWER, firstLevelBlock2.BlockResponse?.RawResponse, StringComparison.Ordinal);
        Assert.Contains("yes", secondLevelBlock.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EchoingModel.IMAGE_ANSWER, secondLevelBlock.BlockResponse?.RawResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithComplexNestedWorkflowWithALoop_ExecutesCorrectlyAsync()
    {
        // Arrange
        var mainWorkflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };

        var section1 = new Section("First Level Workflow 1", mainWorkflow)
        {
            Title = "First Level Workflow 1",
        };

        var section2 = new Section("First Level Workflow 2", mainWorkflow)
        {
            Title = "First Level Workflow 2",
        };

        var section3 = new Section("Second Level Workflow", mainWorkflow)
        {
            Title = "Second Level Workflow",
        };

        var firstLevelBlock1 = new Block(BlockTypes.AiQuery, section1, "firstLevelBlock1")
        {
            Name = "First Level Block 1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Reply with \"{Answer}\"",
                SystemPromptTemplate = "You strictly follow the instructions",
                MaxTokens = 10,
                ShouldUsePageImages = true,
            },
        };
        _ = new TextInput(firstLevelBlock1, "Answer", "Yes");

        var firstLevelBlock2 = new Block(BlockTypes.AiQuery, section1, "firstLevelBlock2")
        {
            Name = "First Level Block 2",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "{Image}\nWhat is on the image",
                SystemPromptTemplate = "You are an assistant who answers to questions about images in ONE word",
                MaxTokens = 10,
                ShouldUsePageImages = true,
            },
        };
        _ = new RagSettings(firstLevelBlock2) { Type = RagTypes.WholeDocument };
        var fileMetadata = new FileMetadata("IniaAi", "elephant.jpg", _fixture.ImageSample.BinaryData.ToArray().Length, StorageProvider.Database, Guid.NewGuid().ToString());
        var doc = new Document(fileMetadata, MediaType.ImageJpeg);
        var page = new Page(doc, 1, 1) { Image = _fixture.ImageSample };
        firstLevelBlock2.Documents.Add(doc);

        _ = new Block(BlockTypes.Merge, section1)
        {
            Name = "Funds List",
            ReplacementTag = "fundsList",
            Properties = new MergeProperties
            {
                TextTemplate =
                """
                Item1
                Item2
                """,
            },
        };

        _ = new Block(BlockTypes.LoopStart, section3)
        {
            Name = "Loop start",
            Properties = new LoopStartProperties
            {
                ListTitle = "Funds",
                LoopedListTemplate = "{fundsList}",
            },
        };

        _ = new Block(BlockTypes.Merge, section3)
        {
            Name = "Q2",
            ReplacementTag = "loopedBlock1",
            Properties = new MergeProperties
            {
                TextTemplate = "{Funds}",
            },
        };

        _ = new Block(BlockTypes.CalculateNumber, section3)
        {
            Name = "Sum",
            ReplacementTag = "loopedBlock2",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "11 * 11",
            },
        };

        _ = new Block(BlockTypes.Merge, section3)
        {
            Name = "Sum",
            ReplacementTag = "loopedBlock3",
            Properties = new MergeProperties
            {
                TextTemplate = "{loopedBlock1}_{loopedBlock2}",
            },
        };

        _ = new Block(BlockTypes.LoopEnd, section3)
        {
            Name = "Loop end",
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(mainWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(mainWorkflow.GetAllBlocks().All(block => block.BlockResponse is not null));
        Assert.Contains("yes", firstLevelBlock1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EchoingModel.IMAGE_ANSWER, firstLevelBlock2.BlockResponse?.RawResponse, StringComparison.Ordinal);
        Assert.Equal(8, section3.Blocks.Count);
        // Iteration 0: Merge block + cloned blocks for Item1
        Assert.Equal(BlockTypes.Merge, section3[0].Type);
        Assert.Contains("Item1", section3[0].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Item1", section3[1].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("121", section3[2].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Item1_121", section3[3].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        // Iteration 1: Merge block + cloned blocks for Item2
        Assert.Equal(BlockTypes.Merge, section3[4].Type);
        Assert.Contains("Item2", section3[4].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Item2", section3[5].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("121", section3[6].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Item2_121", section3[7].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithComplexNestedWorkflowWithALoopAndOutputSource_ExecutesCorrectlyAsync()
    {
        // Arrange
        var mainWorkflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };

        var child1 = new Section("Child 1", mainWorkflow)
        {
            Title = "Child 1",
        };

        var child2 = new Section("Child 2", mainWorkflow)
        {
            Title = "Child 2",
        };

        var child3 = new Section("Child 3", mainWorkflow)
        {
            Title = "Child 3",
        };

        _ = new Block(BlockTypes.Merge, child1)
        {
            Name = "",
            ReplacementTag = "listSource",
            Properties = new MergeProperties
            {
                TextTemplate = "Item1\nItem2\nItem3",
            },
        };

        _ = new Block(BlockTypes.LoopStart, child2)
        {
            Name = "",
            ReplacementTag = "block_0",
            Properties = new LoopStartProperties
            {
                ListTitle = "Items",
                LoopedListTemplate = "{listSource}",
            },
        };

        _ = new Block(BlockTypes.Merge, child2)
        {
            Name = "",
            ReplacementTag = "loop1",
            IsOutputSource = true,
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{Items}</answer>",
            },
        };

        _ = new Block(BlockTypes.Merge, child2)
        {
            Name = "",
            ReplacementTag = "loop2",
            Properties = new MergeProperties
            {
                TextTemplate = "{loop1}",
            },
        };

        _ = new Block(BlockTypes.Merge, child2)
        {
            Name = "",
            ReplacementTag = "block_1",
            Properties = new MergeProperties
            {
                TextTemplate = "Loop1 answer:\n{loop1.answer}",
            },
        };

        _ = new Block(BlockTypes.LoopEnd, child2)
        {
            Name = "",
            ReplacementTag = "block_2",
        };

        _ = new Block(BlockTypes.Merge, child3)
        {
            Name = "",
            ReplacementTag = "block_0",
            Properties = new MergeProperties
            {
                TextTemplate = "Loop1 output:\n{loop1}\n\nLoop2 output:\n{loop2}",
            },
        };

        string expectedOutput =
        """
        Loop1 output:
        <answer>Item1</answer>
        <answer>Item2</answer>
        <answer>Item3</answer>

        Loop2 output:
        <answer>Item1</answer>
        <answer>Item2</answer>
        <answer>Item3</answer>
        """;

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(mainWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(12, child2.Blocks.Count);
        // Iteration 0: Merge block + cloned blocks for Item1
        Assert.Equal(BlockTypes.Merge, child2[0].Type);
        Assert.Contains("Item1", child2[0].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<answer>Item1</answer>", child2[1].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<answer>Item1</answer>", child2[2].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Loop1 answer:\nItem1", child2[3].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        // Iteration 1: Merge block + cloned blocks for Item2
        Assert.Equal(BlockTypes.Merge, child2[4].Type);
        Assert.Contains("Item2", child2[4].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<answer>Item2</answer>", child2[5].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<answer>Item2</answer>", child2[6].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Loop1 answer:\nItem2", child2[7].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        // Iteration 2: Merge block + cloned blocks for Item3
        Assert.Equal(BlockTypes.Merge, child2[8].Type);
        Assert.Contains("Item3", child2[8].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<answer>Item3</answer>", child2[9].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<answer>Item3</answer>", child2[10].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Loop1 answer:\nItem3", child2[11].BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedOutput, child3[0].BlockResponse?.RawResponse, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithGlobalVariables_ExecutesCorrectlyAsync()
    {
        // Arrange
        var workflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };

        _ = new WorkflowVariable(workflow, "SpecifiedGlobalVariable")
        {
            Type = WorkflowVariableTypes.String,
            DefaultValue = "Default Value",
        };
        _ = new WorkflowVariable(workflow, "UnspecifiedGlobalVariable")
        {
            Type = WorkflowVariableTypes.String,
            DefaultValue = "Default Value",
        };
        _ = new WorkflowVariable(workflow, "UnspecifiedGlobalVariableNoDefault")
        {
            Type = WorkflowVariableTypes.String,
        };

        var section = new Section("Section", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Block 1",
            Properties = new MergeProperties
            {
                TextTemplate = "Specified global variable: {SpecifiedGlobalVariable}. Default value: {UnspecifiedGlobalVariable}. Default value no default: {UnspecifiedGlobalVariableNoDefault}",
            },
        };

        Dictionary<string, string> globalVariableValues = new()
        {
            { "SpecifiedGlobalVariable", "Test Value" },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: globalVariableValues, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        string? actualResult = block.BlockResponse?.RawResponse;
        string expectedResult = $"Specified global variable: Test Value. Default value: Default Value. Default value no default: No data available";

        Assert.Equal(expectedResult, actualResult);
    }

    #region Counterparty Transformation Tests

    [Fact]
    public async Task ExecuteWorkflowAsync_WithCounterpartyTransformation_ExecutesCorrectlyAsync()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block1 = new Block(BlockTypes.Merge, section)
        {
            Name = "Block 1",
            Properties = new MergeProperties
            {
                TextTemplate = @"<answer>{Counterparty}, {Counterparty.Name}, {Counterparty.Email} - {PeerCompanies}, {PeerCompanies.Name}, {PeerCompanies.Email}</answer>",
            },
        };
        _ = new TextInput(block1, "section1SetupBlock1", "Yes");

        IDbContextFactory<UserDataDbContext> userDataDbContextFactory = _serviceScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = userDataDbContextFactory.CreateDbContext();

        List<Counterparty> counterpartiesDb = await db.Counterparties.Where(x => x.OrganizationId == _fixture.GetOrganization().Id).AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

        Counterparty? selectedValueForCounterparty = counterpartiesDb.FirstOrDefault();
        Counterparty? selectedValueForPeerCompany = counterpartiesDb.LastOrDefault();

        _workflowHandler.CounterpartyId = selectedValueForCounterparty?.Id;
        _workflowHandler.PeerCompanyIds = selectedValueForPeerCompany?.Id is not null ? [selectedValueForPeerCompany.Id] : null;
        // Act

        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var props = section.Blocks.FirstOrDefault()?.Properties as MergeProperties;
        string? actualResult = props?.ExecutionState?.Text;
        string expectedResult = string.Format("<answer>{0}, {0}, {1} - {2}, {2}, {3}</answer>", selectedValueForCounterparty?.Name, selectedValueForCounterparty?.Email, selectedValueForPeerCompany?.Name, selectedValueForPeerCompany?.Email);

        Assert.Equal(expectedResult, actualResult);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_PeerCompaniesIndexedAndArrayPlaceholders_ResolvesCorrectly()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Test Block",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{PeerCompanies[1].Name}, {PeerCompanies[].Id}</answer>",
            },
        };
        var props = block.Properties as MergeProperties;
        _ = new TextInput(block, "input", "test");

        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        List<Counterparty> peerCompanies = await db.Counterparties
            .Where(c => c.OrganizationId == _fixture.GetOrganization().Id)
            .Take(3)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.True(peerCompanies.Count >= 2, "At least 2 peer companies required");

        _workflowHandler.PeerCompanyIds = [.. peerCompanies.Select(p => p.Id)];

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        string? actual = props?.ExecutionState?.Text;
        string expected = $"<answer>{peerCompanies[0].Name}, {string.Join(", ", peerCompanies.Select(p => p.Id))}</answer>";
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_PeerCompaniesIndexOutOfRange_ResolvesNullGracefully()
    {
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Index Out of Range",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{PeerCompanies[10000].Name}</answer>",
            },
        };
        var props = block.Properties as MergeProperties;

        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        List<Counterparty> peerCompanies = await db.Counterparties
            .Where(c => c.OrganizationId == _fixture.GetOrganization().Id)
            .Take(2)
            .ToListAsync(TestContext.Current.CancellationToken);

        _workflowHandler.PeerCompanyIds = [.. peerCompanies.Select(p => p.Id)];

        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("<answer>No data available</answer>", props?.ExecutionState?.Text);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_InvalidPeerCompaniesProperty_IgnoresSilently()
    {
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Invalid Property",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{PeerCompanies[1].NonExistentProp}</answer>",
            },
        };
        var props = block.Properties as MergeProperties;

        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        List<Counterparty> peerCompanies = await db.Counterparties
            .Where(c => c.OrganizationId == _fixture.GetOrganization().Id)
            .Take(2)
            .ToListAsync(TestContext.Current.CancellationToken);

        _workflowHandler.PeerCompanyIds = [.. peerCompanies.Select(p => p.Id)];

        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("<answer>No data available</answer>", props?.ExecutionState?.Text);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_EmptyPeerCompaniesIds_ReturnsEmpty()
    {
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Empty Peer Companies",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{PeerCompanies[].Name}</answer>",
            },
        };

        _workflowHandler.PeerCompanyIds = [];

        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        var props = block.Properties as MergeProperties;
        Assert.Equal("<answer>No data available</answer>", props?.ExecutionState?.Text);
    }

    #endregion

    #region Organization Transformation Tests

    [Fact]
    public async Task ExecuteWorkflowAsync_WithOrganizationName_ResolvesCorrectly()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Org Name Block",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{Organization.Name}</answer>",
            },
        };
        _ = new TextInput(block, "input", "test");

        Organization org = _fixture.GetOrganization();

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], org.Id, Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var props = block.Properties as MergeProperties;
        Assert.Equal($"<answer>{org.Name}</answer>", props?.ExecutionState?.Text);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithOrganizationDefaultProperty_ResolvesToName()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Org Default Block",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{Organization}</answer>",
            },
        };
        _ = new TextInput(block, "input", "test");

        Organization org = _fixture.GetOrganization();

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], org.Id, Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var props = block.Properties as MergeProperties;
        Assert.Equal($"<answer>{org.Name}</answer>", props?.ExecutionState?.Text);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithOrganizationInvalidProperty_ResolvesToNoData()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Org Invalid Property Block",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{Organization.NonExistent}</answer>",
            },
        };
        _ = new TextInput(block, "input", "test");

        Organization org = _fixture.GetOrganization();

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], org.Id, Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var props = block.Properties as MergeProperties;
        Assert.Equal("<answer>No data available</answer>", props?.ExecutionState?.Text);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithOrganizationAndCounterparty_BothResolveCorrectly()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("", workflow);
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Org + Counterparty Block",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>{Organization.Name} - {Counterparty.Name}</answer>",
            },
        };
        _ = new TextInput(block, "input", "test");

        Organization org = _fixture.GetOrganization();

        IDbContextFactory<UserDataDbContext> dbFactory = _serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext db = dbFactory.CreateDbContext();

        Counterparty? counterparty = await db.Counterparties
            .Where(c => c.OrganizationId == org.Id)
            .AsNoTracking()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(counterparty);

        _workflowHandler.CounterpartyId = counterparty.Id;

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], org.Id, Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var props = block.Properties as MergeProperties;
        Assert.Equal($"<answer>{org.Name} - {counterparty.Name}</answer>", props?.ExecutionState?.Text);
    }

    #endregion

    [Fact]
    public async Task PopulatePlaceholderStringFromWorkflow_WithEmptyOrErrorResponse_ReturnsDefaultEmptyResponseAsync()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("Test Workflow", workflow);

        _ = new Block(BlockTypes.Merge, section, "nullResponseBlock")
        {
            Name = "Null Response Block",
        };

        _ = new Block(BlockTypes.Merge, section, "emptyResponseBlock")
        {
            Name = "Empty Response Block",
            Properties = new MergeProperties
            {
                TextTemplate = "\n",
            },
        };

        _ = new Block(BlockTypes.Merge, section, "errorResponseBlock")
        {
            Name = "Error Response Block",
            Properties = new MergeProperties
            {
                TextTemplate = $"{IChatConnector.ERROR_PREFIX}Some error occurred",
            },
        };

        _ = new Block(BlockTypes.Merge, section, "emptyPropertyBlock")
        {
            Name = "Empty Property Block",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>\n\n\n</answer>",
            },
        };

        var referenceBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Reference Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Null response: {nullResponseBlock}. Empty response: {emptyResponseBlock}. Error response: {errorResponseBlock}. Empty property: {emptyPropertyBlock.answer}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        string? actualResult = referenceBlock.BlockResponse?.RawResponse;
        string expectedResult = $"Null response: No data available. " +
            $"Empty response: No data available. " +
            $"Error response: No data available. " +
            $"Empty property: No data available";

        Assert.Equal(expectedResult, actualResult);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WhenRequiredDocumentsNotProvided_SkipsSectionAsync()
    {
        // Arrange
        var mainWorkflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };

        var section1 = new Section("Section 1", mainWorkflow)
        {
            Title = "Section 1",
        };

        var section2 = new Section("Section 2", mainWorkflow)
        {
            Title = "Section 2",
        };

        var block1 = new Block(BlockTypes.Merge, section1, "block1")
        {
            Name = "Block 1",
            Properties = new MergeProperties
            {
                TextTemplate = "This is section 1's content",
            },
        };

        var block2 = new Block(BlockTypes.AiQuery, section2, "block2")
        {
            Name = "Block 2",
            SupportedDocumentClasses = ["Any"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Process this document",
                SystemPromptTemplate = "You are an assistant",
                ShouldUseAdvancedModel = false,
                MaxTokens = 100,
            },
        };
        // Add RagSettings but don't add documents to cause section2 to be skipped
        _ = new RagSettings(block2) { Type = RagTypes.WholeDocument };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(mainWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block1.BlockResponse); // Section 1 should be executed
        Assert.Null(block2.BlockResponse);    // Section 2 should be skipped
        Assert.Contains("This is section 1's content", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithReferencesToSkippedSection_ReturnsDefaultEmptyPlaceholderAsync()
    {
        // Arrange
        var mainWorkflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };

        var section1 = new Section("Section 1", mainWorkflow)
        {
            Title = "Section 1",
        };

        var section2 = new Section("Section 2", mainWorkflow)
        {
            Title = "Section 2",
        };

        var section3 = new Section("Section 3", mainWorkflow)
        {
            Title = "Section 3",
        };

        var block1 = new Block(BlockTypes.Merge, section1, "block1")
        {
            Name = "Block 1",
            Properties = new MergeProperties
            {
                TextTemplate = "Content from section 1",
            },
        };

        var block2 = new Block(BlockTypes.AiQuery, section2, "block2")
        {
            Name = "Block 2",
            SupportedDocumentClasses = ["Any"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Process this document",
                SystemPromptTemplate = "You are an assistant",
                ShouldUseAdvancedModel = false,
                MaxTokens = 100,
            },
        };
        // Add RagSettings but don't add documents to cause section2 to be skipped
        _ = new RagSettings(block2) { Type = RagTypes.WholeDocument };

        var block3 = new Block(BlockTypes.Merge, section3, "block3")
        {
            Name = "Block 3",
            Properties = new MergeProperties
            {
                TextTemplate = "Section 1: {block1}\nSection 2: {block2}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(mainWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block1.BlockResponse);
        Assert.Null(block2.BlockResponse);
        Assert.NotNull(block3.BlockResponse);

        // Section 3's block should have content from section 1 but default placeholder for section 2
        Assert.Contains("Section 1: Content from section 1", block3.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Section 2: No data available", block3.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithMultipleSkippedSections_ExecutesRemainingCorrectlyAsync()
    {
        // Arrange
        var mainWorkflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };

        var section1 = new Section("Section 1", mainWorkflow)
        {
            Title = "Section 1",
        };

        var section2 = new Section("Section 2", mainWorkflow)
        {
            Title = "Section 2",
        };

        var section3 = new Section("Section 3", mainWorkflow)
        {
            Title = "Section 3",
        };

        var section4 = new Section("Section 4", mainWorkflow)
        {
            Title = "Section 4",
        };

        var block1 = new Block(BlockTypes.Merge, section1, "block1")
        {
            Name = "Block 1",
            Properties = new MergeProperties
            {
                TextTemplate = "Content from section 1",
            },
        };

        var block2 = new Block(BlockTypes.AiQuery, section2, "block2")
        {
            Name = "Block 2",
            SupportedDocumentClasses = ["Any"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Process this document",
                SystemPromptTemplate = "You are an assistant",
                ShouldUseAdvancedModel = false,
                MaxTokens = 100,
            },
        };
        // Add RagSettings but don't add documents to cause section2 to be skipped
        _ = new RagSettings(block2) { Type = RagTypes.WholeDocument };

        var block3 = new Block(BlockTypes.AiQuery, section3, "block3")
        {
            Name = "Block 3",
            SupportedDocumentClasses = ["Any"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Process another document",
                SystemPromptTemplate = "You are an assistant",
                ShouldUseAdvancedModel = false,
                MaxTokens = 100,
            },
        };
        // Add RagSettings but don't add documents to cause section3 to be skipped
        _ = new RagSettings(block3) { Type = RagTypes.WholeDocument };

        var block4 = new Block(BlockTypes.Merge, section4, "block4")
        {
            Name = "Block 4",
            Properties = new MergeProperties
            {
                TextTemplate = "Summary: Section 1: {block1}, Section 2: {block2}, Section 3: {block3}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(mainWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block1.BlockResponse);
        Assert.Null(block2.BlockResponse);
        Assert.Null(block3.BlockResponse);
        Assert.NotNull(block4.BlockResponse);

        // Section 4's block should have content from section 1 but default placeholders for sections 2 and 3
        Assert.Contains("Section 1: Content from section 1", block4.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Section 2: No data available", block4.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Section 3: No data available", block4.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_AiQueryBlockWithIterationsWithTocRag_ExecutesCorrectlyAsync()
    {
        // Arrange
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "Ostriches are fast runners" };
        _ = new Page(doc1, 2, 2) { RawText = "Penguins are flightless birds" };
        _ = new Page(doc1, 3, 3) { RawText = "Polar bears are white" };
        _ = new Page(doc1, 4, 4) { RawText = "Tigers are big cats" };
        _ = new Page(doc1, 5, 5) { RawText = "Dogs are loyal" };

        var workflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };
        var section = new Section("Section", workflow);

        var block1 = new Block(BlockTypes.AiQuery, section, "block2")
        {
            Name = "Block 2",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "List the animals in the document",
                ShouldUseAdvancedModel = false,
                MaxTokens = 100,
                ShouldChunkLargeInputContexts = true,
            },
            SupportedDocumentClasses = ["Any"],
        };
        _ = new RagSettings(block1)
        {
            Type = RagTypes.UseTableOfContents,
            TableOfContentsInput =
            """
            {
            "Sections":
                [
                    {
                        "Title": "Birds",
                        "StartPage": "1",
                        "EndPage": "2"
                    },
                    {
                        "Title": "Mammals",
                        "StartPage": 3,
                        "EndPage": 5
                    }
                ]
            }
            """,
        };
        block1.Documents.Add(doc1);

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block1.BlockResponse);
        Assert.Contains("Iteration 1 (Page 0, 1)", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ostriches", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Penguins", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Iteration 2 (Page 2, 3, 4)", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Polar bears", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tigers", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dogs", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Iteration 3", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_AiQueryBlockWithIterationsWithNPagesRag_ExecutesCorrectlyAsync()
    {
        // Arrange
        var doc1 = new Document(new FileMetadata("IniaAi", "Doc1.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        _ = new Page(doc1, 1, 1) { RawText = "Ostriches are fast runners" };
        _ = new Page(doc1, 2, 2) { RawText = "Penguins are flightless birds" };
        _ = new Page(doc1, 3, 3) { RawText = "Polar bears are white" };
        _ = new Page(doc1, 4, 4) { RawText = "Tigers are big cats" };
        _ = new Page(doc1, 5, 5) { RawText = "Dogs are loyal" };
        _ = new Page(doc1, 6, 6) { RawText = "Cats are independent" };

        var workflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };
        var section = new Section("Main Workflow", workflow)
        {
            Title = "Main Workflow",
        };

        var block1 = new Block(BlockTypes.AiQuery, section, "block2")
        {
            Name = "Block 2",
            SupportedDocumentClasses = ["Any"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate =
                """
                List the animals in the document
                {{InputContext}}
                """,
                ShouldUseAdvancedModel = false,
                MaxTokens = 100,
                ShouldChunkLargeInputContexts = true,
                ChunkSize = 2,
            },
        };
        _ = new RagSettings(block1)
        {
            Type = RagTypes.WholeDocument,
        };
        block1.Documents.Add(doc1);

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block1.BlockResponse);
        Assert.Contains("Iteration 1", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ostriches", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Penguins", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Iteration 2", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Polar bears", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tigers", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Iteration 3", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dogs", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cats", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Iteration 4", block1.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertResponseContainsLines(string? response, params string[] expectedLines)
    {
        Assert.NotNull(response);

        foreach (string expectedLine in expectedLines)
        {
            Assert.Contains(expectedLine, response, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertResponseOmits(string? response, params string[] absentTexts)
    {
        Assert.NotNull(response);

        foreach (string absentText in absentTexts)
        {
            Assert.DoesNotContain(absentText, response, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task GetPdfEmbeddingsAsync(List<Document> documents)
    {
        IEmbeddingsConnector embeddingsConnector = _testServiceProvider
            .GetRequiredService<IEmbeddingsConnector>();

        foreach (Document document in documents)
        {
            var pagesToEmbed = document
                .Pages
                .Where(p => p.EmbeddingVector is null && !string.IsNullOrWhiteSpace(p.Text))
                .ToList();

            if (pagesToEmbed.Count == 0)
            {
                return;
            }

            var texts = pagesToEmbed.Select(p => p.Text ?? "").ToList();

            List<double[]> allEmbeddings = [];

            int pagesInBatch = 20;

            for (int i = 0; i < texts.Count; i += pagesInBatch)
            {
                var batch = texts.Skip(i).Take(pagesInBatch).ToList();

                List<double[]> batchEmbeddings = (await embeddingsConnector
                    .EmbedAsync(batch)
                    .ConfigureAwait(false)).Vectors;

                allEmbeddings.AddRange(batchEmbeddings);
            }

            for (int i = 0; i < pagesToEmbed.Count; i++)
            {
                pagesToEmbed[i].EmbeddingVector = new(embeddingsConnector.ModelName, allEmbeddings[i]);
            }
        }
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithStopTermInTextInput_SkipsBlockExecution()
    {
        // Arrange
        var workflow = new Workflow("Root");
        var sectionWorkflow = new Section("Section", workflow);
        var block = new Block(BlockTypes.AiQuery, sectionWorkflow)
        {
            Name = "Block with STOP_TERM",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Some prompt",
                SystemPromptTemplate = "Some system prompt",
                MaxTokens = 2,
            },
        };
        var textInput = new TextInput(block, "TestInput", $"This contains {StopMarker.VALUE} stop term");
        textInput.SetExecutionState(false, []);

        // Act
        _ = await _workflowHandler.ExecuteBlockAsync(workflow.Sections.First(s => s.Id == sectionWorkflow.Id), 0, [], _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithStopTermInsideAnIdInTextInput_RunsTheBlock()
    {
        var workflow = new Workflow("Root");
        var sectionWorkflow = new Section("Section", workflow);
        var block = new Block(BlockTypes.Merge, sectionWorkflow, "mergeBlock")
        {
            Name = "Block whose input carries an id",
            ReplacementTag = "mergeBlock",
            Properties = new MergeProperties
            {
                TextTemplate = "Merged output",
            },
        };
        var textInput = new TextInput(block, "TestInput", $"Workflow id {ID_CONTAINING_STOP_TERM}");
        textInput.SetExecutionState(false, []);

        _ = await _workflowHandler.ExecuteBlockAsync(workflow.Sections.First(s => s.Id == sectionWorkflow.Id), 0, [], _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithTwoBlocksWithTheSameReplacementTag_ReferencesTheBlockThatHasResponse1Async()
    {
        // Arrange
        var rootWorkflow = new Workflow("Test Workflow");

        var section1 = new Section("Section 1", rootWorkflow)
        {
            Title = "Section 1",
        };

        var section2 = new Section("Section 2", rootWorkflow)
        {
            Title = "Section 2",
        };

        var section3 = new Section("Section 3", rootWorkflow)
        {
            Title = "Section 3",
        };

        var block1 = new Block(BlockTypes.AiQuery, section1, "block1")
        {
            Name = "Block 1",
            ReplacementTag = "sharedTag",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "This is section 1's content",
            },
        };

        _ = new RagSettings(block1)
        {
            Type = RagTypes.WholeDocument,
        };

        var block2 = new Block(BlockTypes.Merge, section2, "block2")
        {
            Name = "Block 2",
            ReplacementTag = "sharedTag",
            Properties = new MergeProperties
            {
                TextTemplate = "This is section 2's content",
            },
        };

        var referenceBlock = new Block(BlockTypes.Merge, section3, "referenceBlock")
        {
            Name = "Reference Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Section 1: {sharedTag}, Section 2: {sharedTag}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(rootWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block1.BlockResponse);
        Assert.NotNull(block2.BlockResponse);
        Assert.NotNull(referenceBlock.BlockResponse);
        Assert.Contains("Section 1: This is section 2's content", referenceBlock.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Section 2: This is section 2's content", referenceBlock.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithTwoBlocksWithTheSameReplacementTag_ReferencesTheBlockThatHasResponse2Async()
    {
        // Arrange
        var rootWorkflow = new Workflow("Test Workflow");

        var section1 = new Section("Section 1", rootWorkflow)
        {
            Title = "Section 1",
        };

        var section2 = new Section("Section 2", rootWorkflow)
        {
            Title = "Section 2",
        };

        var section3 = new Section("Section 3", rootWorkflow)
        {
            Title = "Section 3",
        };

        var block1_ = new Block(BlockTypes.Merge, section1, "block1")
        {
            Name = "Block 1",
            ReplacementTag = "sharedTag",
            Properties = new MergeProperties
            {
                TextTemplate = "This is section 1's content",
            },
        };

        var block2 = new Block(BlockTypes.AiQuery, section2, "block2")
        {
            Name = "Block 2",
            ReplacementTag = "sharedTag",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "This is section 2's content",
            },
        };

        _ = new RagSettings(block2)
        {
            Type = RagTypes.WholeDocument,
        };

        var referenceBlock = new Block(BlockTypes.Merge, section3, "referenceBlock")
        {
            Name = "Reference Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Section 1: {sharedTag}, Section 2: {sharedTag}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(rootWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block2.BlockResponse);
        Assert.NotNull(block1_.BlockResponse);
        Assert.NotNull(referenceBlock.BlockResponse);
        Assert.Contains("Section 1: This is section 1's content", referenceBlock.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Section 2: This is section 1's content", referenceBlock.BlockResponse?.RawResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithWorkflowTextInputTemplate_SetsExecutionStateCorrectly()
    {
        // Arrange
        var workflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };
        var section = new Section("Test Workflow", workflow)
        {
            Title = "Test Workflow",
            TextInputTemplate = "Workflow input: {testInput}"
        };

        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Test Block",
            ReplacementTag = "testBlock",
            Properties = new MergeProperties
            {
                TextTemplate = "Block executed successfully",
            },
        };

        var inputs = new Dictionary<string, string>
        {
            ["testInput"] = "Hello World"
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: inputs, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(section.ExecutionState);
        Assert.Equal("Workflow input: Hello World", section.ExecutionState.TextInput);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithWorkflowContainingStopTerm_SkipsWorkflowExecution()
    {
        // Arrange
        var workflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };
        var section = new Section("Test Workflow", workflow)
        {
            Title = "Test Workflow",
            TextInputTemplate = $"This contains {StopMarker.VALUE} stop term"
        };

        var block = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            ReplacementTag = "testBlock",
            Properties = new MergeProperties
            {
                TextTemplate = "This should not execute",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(section.ExecutionState);
        Assert.Contains(StopMarker.VALUE, section.ExecutionState.TextInput);
        Assert.Null(block.BlockResponse); // Block should not have executed
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithStopTermInsideAnIdInSectionInput_RunsTheSection()
    {
        var workflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };
        var section = new Section("Test Workflow", workflow)
        {
            Title = "Test Workflow",
            TextInputTemplate = $"Workflow id {ID_CONTAINING_STOP_TERM}"
        };

        var block = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            ReplacementTag = "testBlock",
            Properties = new MergeProperties
            {
                TextTemplate = "This should execute",
            },
        };

        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(section.ExecutionState);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithNestedWorkflowExecutionState_PropagatesCorrectly()
    {
        // Arrange
        var mainWorkflow = new Workflow("Main Workflow")
        {
            Title = "Main Workflow",
        };

        var childSection = new Section("Child Workflow", mainWorkflow)
        {
            Title = "Child Workflow",
            TextInputTemplate = "Child: {childInput}"
        };

        _ = new Block(BlockTypes.Merge, childSection, "childBlock")
        {
            Name = "Child Block",
            ReplacementTag = "childBlock",
            Properties = new MergeProperties
            {
                TextTemplate = "Child block executed",
            },
        };

        var inputs = new Dictionary<string, string>
        {
            ["childInput"] = "Child Value"
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(mainWorkflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: inputs, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(mainWorkflow.ExecutionState);
        Assert.NotNull(childSection.ExecutionState);
        Assert.Equal("Child: Child Value", childSection.ExecutionState.TextInput);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithWorkflowVariableInTextInputTemplate_ReplacesCorrectly()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow")
        {
            Title = "Test Workflow",
        };

        _ = new WorkflowVariable(workflow)
        {
            ReplacementTag = "customVar",
            DefaultValue = "Default Value"
        };

        var section = new Section("Section", workflow)
        {
            Title = "Section",
            TextInputTemplate = "Date: {ReportDate}, Custom: {customVar}",
        };

        _ = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            ReplacementTag = "testBlock",
            Properties = new MergeProperties
            {
                TextTemplate = "Block executed",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(section.ExecutionState);
        Assert.Contains("Default Value", section.ExecutionState.TextInput);
        Assert.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd"), section.ExecutionState.TextInput);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithBlockResponseTypeAccess_ReturnsCorrectResponseTypes()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        var sourceBlock = new Block(BlockTypes.Merge, section, "sourceBlock")
        {
            Name = "Source Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Raw content",
            },
        };

        var testBlock = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Raw: {sourceBlock}, Json: {sourceBlock.Json}, Html: {sourceBlock.Html}, Markdown: {sourceBlock.Markdown}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Set different response types on the source block
        sourceBlock.BlockResponse!.JsonResponse = "{\"test\": \"json_value\"}";
        sourceBlock.BlockResponse!.HtmlResponse = "<p>html_content</p>";
        sourceBlock.BlockResponse!.MarkdownResponse = "**markdown_content**";

        // Re-execute the test block to see the different response types
        _ = await _workflowHandler.ExecuteBlockAsync(workflow.Sections.First(s => s.Id == section.Id), 1, [], _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(testBlock.BlockResponse);
        string response = testBlock.BlockResponse.RawResponse!;
        Assert.Contains("Raw: Raw content", response);
        Assert.Contains("Json: {\"test\": \"json_value\"}", response);
        Assert.Contains("Html: <p>html_content</p>", response);
        Assert.Contains("Markdown: **markdown_content**", response);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithNullResponseTypes_ReturnsDefaultEmptyValue()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        _ = new Block(BlockTypes.Merge, section, "sourceBlock")
        {
            Name = "Source Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Raw content only",
            },
        };

        var testBlock = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Json: {sourceBlock.Json}, Html: {sourceBlock.Html}, Markdown: {sourceBlock.Markdown}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(testBlock.BlockResponse);
        string response = testBlock.BlockResponse.RawResponse!;
        Assert.Contains("Json: No data available", response);
        Assert.Contains("Html: No data available", response);
        Assert.Contains("Markdown: No data available", response);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithJsonResponsePropertyAccess_ExtractsJsonProperties()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        var jsonBlock = new Block(BlockTypes.Merge, section, "jsonBlock")
        {
            Name = "JSON Block",
            Properties = new MergeProperties
            {
                TextTemplate = "JSON content",
            },
        };

        var propertyAccessBlock = new Block(BlockTypes.Merge, section, "propertyAccessBlock")
        {
            Name = "Property Access Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Name: {jsonBlock.Json.name}, Age: {jsonBlock.Json.age}, City: {jsonBlock.Json.address.city}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Set JSON response with nested properties
        jsonBlock.BlockResponse!.JsonResponse = "{\"name\": \"John Doe\", \"age\": 30, \"address\": {\"city\": \"New York\", \"zip\": \"10001\"}}";

        // Re-execute the property access block
        _ = await _workflowHandler.ExecuteBlockAsync(workflow.Sections.First(s => s.Id == section.Id), 1, [], _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(propertyAccessBlock.BlockResponse);
        string response = propertyAccessBlock.BlockResponse.RawResponse!;
        Assert.Contains("Name: John Doe", response);
        Assert.Contains("Age: 30", response);
        Assert.Contains("City: New York", response);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithInvalidJsonResponsePropertyAccess_ReturnsEmptyForMissingProperties()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        var jsonBlock = new Block(BlockTypes.Merge, section, "jsonBlock")
        {
            Name = "JSON Block",
            Properties = new MergeProperties
            {
                TextTemplate = "JSON content",
            },
        };

        var propertyAccessBlock = new Block(BlockTypes.Merge, section, "propertyAccessBlock")
        {
            Name = "Property Access Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Name: {jsonBlock.Json.name}, Missing: {jsonBlock.Json.nonexistent.property}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Set JSON response without all expected properties
        jsonBlock.BlockResponse!.JsonResponse = "{\"name\": \"John Doe\"}";

        // Re-execute the property access block
        _ = await _workflowHandler.ExecuteBlockAsync(workflow.Sections.First(s => s.Id == section.Id), 1, [], _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(propertyAccessBlock.BlockResponse);
        string response = propertyAccessBlock.BlockResponse.RawResponse!;
        Assert.Contains("Name: John Doe", response);
        Assert.Contains("Missing: ", response); // Should be empty for missing property
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithStringResponsePropertyAccess_TreatsAsStringPattern()
    {
        // Arrange
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        _ = new Block(BlockTypes.Merge, section, "textBlock")
        {
            Name = "Text Block",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>This is the answer</answer><reasoning>This is reasoning</reasoning>",
            },
        };

        var propertyAccessBlock = new Block(BlockTypes.Merge, section, "propertyAccessBlock")
        {
            Name = "Property Access Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Answer: {textBlock.answer}, Reasoning: {textBlock.reasoning}, Json Answer: {textBlock.Json.answer}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(propertyAccessBlock.BlockResponse);
        string response = propertyAccessBlock.BlockResponse.RawResponse!;
        Assert.Contains("Answer: This is the answer", response);
        Assert.Contains("Reasoning: This is reasoning", response);
        Assert.Contains("Json Answer: ", response); // Should be empty since it's not valid JSON
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithBackwardCompatiblePatterns_StillWorksAsExpected()
    {
        // Arrange - Test that existing patterns like {blockName} and {blockName.property} still work
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        _ = new Block(BlockTypes.Merge, section, "dataBlock")
        {
            Name = "Data Block",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>Test Answer</answer><note>Test Note</note>",
            },
        };

        var testBlock = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Full: {dataBlock}, Answer: {dataBlock.answer}, Note: {dataBlock.note}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(testBlock.BlockResponse);
        string response = testBlock.BlockResponse.RawResponse!;
        Assert.Contains("Full: <answer>Test Answer</answer><note>Test Note</note>", response);
        Assert.Contains("Answer: Test Answer", response);
        Assert.Contains("Note: Test Note", response);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithMixedResponseTypeAndPropertyAccess_HandlesCorrectly()
    {
        // Arrange - Test mixing typed response access with property access
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        var sourceBlock = new Block(BlockTypes.Merge, section, "sourceBlock")
        {
            Name = "Source Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Regular content",
            },
        };

        var testBlock = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Default: {sourceBlock}, Raw Html: {sourceBlock.Html}, Json Name: {sourceBlock.Json.name}, Html Title: {sourceBlock.Html.title}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Set typed responses
        sourceBlock.BlockResponse!.HtmlResponse = "<title>HTML Title</title><body>HTML Body</body>";
        sourceBlock.BlockResponse!.JsonResponse = "{\"name\": \"JSON Name\", \"value\": 42}";

        // Re-execute the test block
        _ = await _workflowHandler.ExecuteBlockAsync(workflow.Sections.First(s => s.Id == section.Id), 1, [], _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(testBlock.BlockResponse);
        string response = testBlock.BlockResponse.RawResponse!;
        Assert.Contains("Default: Regular content", response);
        Assert.Contains("Raw Html: <title>HTML Title</title><body>HTML Body</body>", response);
        Assert.Contains("Json Name: JSON Name", response);
        Assert.Contains("Html Title: ", response); // HTML is not valid JSON, so property access should be empty
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithInvalidResponseTypeConstants_FallsBackToRawResponse()
    {
        // Arrange - Test that invalid response type constants fall back to raw response
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        _ = new Block(BlockTypes.Merge, section, "sourceBlock")
        {
            Name = "Source Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Raw content",
            },
        };

        var testBlock = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Invalid: {sourceBlock.InvalidType}, Unknown: {sourceBlock.Unknown.property}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(testBlock.BlockResponse);
        string response = testBlock.BlockResponse.RawResponse!;
        // Should treat as property access on the raw response since InvalidType is not a valid response type
        Assert.Contains("Invalid: ", response);
        Assert.Contains("Unknown: ", response);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithCaseSensitiveResponseTypes_MatchesExactly()
    {
        // Arrange - Test that response type constants are case sensitive
        var workflow = new Workflow("Test Workflow");
        var section = new Section("Test Workflow", workflow);

        var sourceBlock = new Block(BlockTypes.Merge, section, "sourceBlock")
        {
            Name = "Source Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Raw content",
            },
        };

        var testBlock = new Block(BlockTypes.Merge, section, "testBlock")
        {
            Name = "Test Block",
            Properties = new MergeProperties
            {
                TextTemplate = "Correct: {sourceBlock.Json}, Lower: {sourceBlock.json}, Upper: {sourceBlock.JSON}",
            },
        };

        // Act
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Set JSON response
        sourceBlock.BlockResponse!.JsonResponse = "{\"test\": \"value\"}";

        // Re-execute the test block
        _ = await _workflowHandler.ExecuteBlockAsync(workflow.Sections.First(s => s.Id == section.Id), 1, [], _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(testBlock.BlockResponse);
        string? response = testBlock.BlockResponse.RawResponse;
        Assert.Contains("Correct: {\"test\": \"value\"}", response); // Should match exactly
        Assert.Contains("Lower: ", response); // Should be empty (treated as property access)
        Assert.Contains("Upper: ", response); // Should be empty (treated as property access)
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_BlockExecuted_LogWorkflowIdReferencesExecutedRootNotSection()
    {
        var workflow = new Workflow();
        var section = new Section("Merge Section", workflow);
        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Merge block",
            ReplacementTag = "mergeBlock",
            Properties = new MergeProperties
            {
                TextTemplate = "Static output text",
            },
        };

        const string organizationId = "testOrg";
        string userId = Guid.NewGuid().ToString();

        await _workflowHandler.CreateRootOutputAsync(_userDataUnitOfWork, null, workflow, organizationId, userId);
        _ = await _workflowHandler.ExecuteWorkflowAsync(workflow, [], organizationId, userId, _userDataUnitOfWork, cancellationToken: TestContext.Current.CancellationToken);

        List<WorkflowExecutionLog> logs = [.. (await _userDataUnitOfWork
            .Repository<WorkflowExecutionLog>()
            .FindAsync(log => log.WorkflowExecutionId != null))];

        Assert.NotEmpty(logs);
        Assert.All(logs, log => Assert.Equal(log.WorkflowExecutionId, log.WorkflowId));
    }
}
