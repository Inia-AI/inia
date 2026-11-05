using ErrorOr;
using IniaAi.Application.Queries.WorkflowExecutions.GetExecutionBlocks;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetExecutionBlocks;

public class GetExecutionBlocksQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";
    private const string EXECUTION_ID = "exec-123";
    private const string WORKFLOW_TYPE_ID = "WT-Demo";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly GetExecutionBlocksQueryHandler _handler;

    public GetExecutionBlocksQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new GetExecutionBlocksQueryHandler(unitOfWork);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyAiQueryBlocks_WithCapturedSignatureValues()
    {
        await SeedExecutionSnapshotAsync();

        var query = new GetExecutionBlocksQuery(ORG_ID, USER_ID, EXECUTION_ID);

        ErrorOr<ExecutionBlocksDto> result = await _handler.Handle(
            query,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(EXECUTION_ID, result.Value.WorkflowExecutionId);

        // Only the AI Query blocks should be returned — the Code block must be filtered out.
        Assert.Equal(2, result.Value.Blocks.Count);
        Assert.All(result.Value.Blocks, b => Assert.NotNull(b.SignatureFields));

        ExecutionBlockDto blockWithCapture = result.Value.Blocks.Single(b => b.BlockId == "ai-1");
        Assert.Equal("ai response 1", blockWithCapture.RawResponse);
        ReplayableSignatureFieldDto field = Assert.Single(blockWithCapture.SignatureFields);
        Assert.Equal("COGS_instructions", field.PlaceholderName);
        Assert.Equal("captured-value", field.CapturedResolvedValue);
        Assert.True(field.CanReplay);

        ExecutionBlockDto blockWithoutCapture = result.Value.Blocks.Single(b => b.BlockId == "ai-2");
        ReplayableSignatureFieldDto preCaptureField = Assert.Single(blockWithoutCapture.SignatureFields);
        Assert.Null(preCaptureField.CapturedResolvedValue);
        Assert.False(preCaptureField.CanReplay);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenExecutionBelongsToAnotherOrg()
    {
        await SeedExecutionSnapshotAsync(orgId: OTHER_ORG_ID);

        var query = new GetExecutionBlocksQuery(ORG_ID, USER_ID, EXECUTION_ID);

        ErrorOr<ExecutionBlocksDto> result = await _handler.Handle(
            query,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WorkflowLevelDefinitions_SurfacePerBlockFilteredByTokens()
    {
        // Post-hoist snapshot: definitions live on the workflow, blocks bind via {tokens}.
        var root = new Workflow("Hoisted Exec")
        {
            Id = EXECUTION_ID,
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        _ = new WorkflowSignatureField(root, "COGS_instructions")
        {
            DisplayName = "COGS",
            Description = "cogs desc",
            DefaultValue = "default-cogs",
        };
        _ = new WorkflowSignatureField(root, "unreferenced_field")
        {
            DisplayName = "Unused",
            DefaultValue = "unused-default",
        };
        var section = new Section("Section", root) { Order = 0 };

        AiQueryProperties boundProps = new()
        {
            UserPromptTemplate = "Rules: {COGS_instructions}",
        };
        boundProps.SetExecutionState(asPlaceHolder: false, inputs: []);
        boundProps.ExecutionState!.ResolvedSignatureFieldValues = new Dictionary<string, string>
        {
            ["COGS_instructions"] = "captured-value",
        };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = "ai-bound",
            Name = "Bound Block",
            Order = 0,
            Properties = boundProps,
        };

        // AI Query block referencing no token — no definitions, so it must not surface.
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = "ai-unbound",
            Name = "Unbound Block",
            Order = 1,
            Properties = new AiQueryProperties { UserPromptTemplate = "No tokens here" },
        };

        _ = _context.Workflows.Add(root);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetExecutionBlocksQuery(ORG_ID, USER_ID, EXECUTION_ID);
        ErrorOr<ExecutionBlocksDto> result = await _handler.Handle(
            query,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        ExecutionBlockDto block = Assert.Single(result.Value.Blocks);
        Assert.Equal("ai-bound", block.BlockId);

        // Only the referenced definition surfaces — the unreferenced one is filtered out.
        ReplayableSignatureFieldDto field = Assert.Single(block.SignatureFields);
        Assert.Equal("COGS_instructions", field.PlaceholderName);
        Assert.Equal("COGS", field.DisplayName);
        Assert.Equal("cogs desc", field.Description);
        Assert.Equal("default-cogs", field.DefaultValue);
        Assert.Equal("captured-value", field.CapturedResolvedValue);
        Assert.True(field.CanReplay);
    }

    [Fact]
    public async Task Handle_LegacyInlineSignatureFields_SurfaceWithoutWorkflowLevelDefinitions()
    {
        // Pre-migration snapshot: the inline per-block dict is the only source of
        // definitions, and the workflow carries no workflow-level fields at all.
        var root = new Workflow("Legacy Exec")
        {
            Id = EXECUTION_ID,
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        var section = new Section("Section", root) { Order = 0 };

        AiQueryProperties legacyProps = new()
        {
            UserPromptTemplate = "Rules: resolved",
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["legacy_field"] = new("Legacy", "legacy desc", "legacy-default"),
            },
        };
        legacyProps.SetExecutionState(asPlaceHolder: false, inputs: []);
        legacyProps.ExecutionState!.ResolvedSignatureFieldValues = new Dictionary<string, string>
        {
            ["legacy_field"] = "legacy-captured",
        };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = "ai-legacy",
            Name = "Legacy Block",
            Order = 0,
            Properties = legacyProps,
        };

        _ = _context.Workflows.Add(root);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetExecutionBlocksQuery(ORG_ID, USER_ID, EXECUTION_ID);
        ErrorOr<ExecutionBlocksDto> result = await _handler.Handle(
            query,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        ExecutionBlockDto block = Assert.Single(result.Value.Blocks);
        Assert.Equal("ai-legacy", block.BlockId);

        ReplayableSignatureFieldDto field = Assert.Single(block.SignatureFields);
        Assert.Equal("legacy_field", field.PlaceholderName);
        Assert.Equal("Legacy", field.DisplayName);
        Assert.Equal("legacy desc", field.Description);
        Assert.Equal("legacy-default", field.DefaultValue);
        Assert.Equal("legacy-captured", field.CapturedResolvedValue);
        Assert.True(field.CanReplay);
    }

    private async Task SeedExecutionSnapshotAsync(string orgId = ORG_ID)
    {
        var root = new Workflow("Test Exec")
        {
            Id = EXECUTION_ID,
            OrganizationId = orgId,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        var section = new Section("Section", root) { Order = 0 };

        // AI Query block with captured resolved signature values.
        AiQueryProperties props1 = new()
        {
            UserPromptTemplate = "Rules: captured-value",
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["COGS_instructions"] = new("COGS", "desc", "default-cogs"),
            },
        };
        props1.SetExecutionState(asPlaceHolder: false, inputs: []);
        props1.ExecutionState!.ResolvedSignatureFieldValues = new Dictionary<string, string>
        {
            ["COGS_instructions"] = "captured-value",
        };
        Block block1 = new(BlockTypes.AiQuery, section)
        {
            Id = "ai-1",
            Name = "AI Block 1",
            Order = 0,
            Properties = props1,
        };
        _ = new BlockResponse(block1)
        {
            RawResponse = "ai response 1",
        };

        // AI Query block without captured values (pre-capture run simulation).
        AiQueryProperties props2 = new()
        {
            UserPromptTemplate = "Rules: baked-in value",
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["COGS_instructions"] = new("COGS", "desc", "default-cogs"),
            },
        };
        props2.SetExecutionState(asPlaceHolder: false, inputs: []);
        // ResolvedSignatureFieldValues deliberately left null on the execution state.
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Id = "ai-2",
            Name = "AI Block 2",
            Order = 1,
            Properties = props2,
        };

        // Non-AI-Query block — must NOT appear in the result.
        _ = new Block(BlockTypes.Merge, section)
        {
            Id = "merge-1",
            Name = "Merge Block",
            Order = 2,
        };

        _ = _context.Workflows.Add(root);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
