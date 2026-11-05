using ErrorOr;
using IniaAi.Application.Commands.Signatures.TestSignaturePrompt;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.Signatures.TestSignaturePrompt;

public class TestSignaturePromptCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";
    private const string EXECUTION_ID = "exec-abc";
    private const string BLOCK_ID = "block-abc";
    private const string FIELD_A = "COGS_instructions";
    private const string FIELD_B = "REV_rules";
    private const string OLD_VALUE_A = "Extract COGS net of D&A";
    private const string NEW_VALUE_A = "Extract COGS gross of D&A";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly ScriptedChatConnector _chatConnector;
    private readonly TestSignaturePromptCommandHandler _handler;
    private string _modelAnswer = "stub response";

    public TestSignaturePromptCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _chatConnector = new ScriptedChatConnector(_ => _modelAnswer);
        _handler = new TestSignaturePromptCommandHandler(
            unitOfWork,
            _chatConnector,
            NullLogger<TestSignaturePromptCommandHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_HappyPath_SwapsCapturedValue_AndReturnsAiResponse()
    {
        await SeedExecutionSnapshotAsync(
            orgId: ORG_ID,
            userPrompt: $"Rules: {OLD_VALUE_A}\nAnd proceed.",
            systemPrompt: null,
            captured: new() { [FIELD_A] = OLD_VALUE_A });

        _modelAnswer = "modified output";

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, NEW_VALUE_A);

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(OLD_VALUE_A, result.Value.OldValueUsed);
        Assert.Equal(NEW_VALUE_A, result.Value.NewValueUsed);
        Assert.Equal(1, result.Value.OccurrencesReplaced);
        Assert.Equal("modified output", result.Value.ResponseText);
        // Model id must come from the provider's response metadata, not
        // from the (often null) per-block override.
        Assert.Equal(ScriptedChatConnector.MODEL_ID, result.Value.ModelUsed);

        // Verify the ChatConnector received the swapped prompt.
        ScriptedModelCall call = Assert.Single(_chatConnector.Calls);
        Assert.Contains(NEW_VALUE_A, call.Text);
        Assert.DoesNotContain(OLD_VALUE_A, call.Text);
    }

    [Fact]
    public async Task Handle_PreCaptureRun_ReturnsConflict()
    {
        // Whole dict is null — entire execution predates capture.
        await SeedExecutionSnapshotAsync(
            orgId: ORG_ID,
            userPrompt: "Rules: some baked-in text",
            systemPrompt: null,
            captured: null);

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, NEW_VALUE_A);

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(TestSignaturePromptCommandHandler.ERROR_PRE_CAPTURE_RUN, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_PlaceholderNotInCapturedDict_ReturnsPlaceholderNotCaptured()
    {
        // Dict exists but doesn't carry the placeholder we're asked to replay
        // against (e.g. oversized-skipped, field renamed, stale FE).
        await SeedExecutionSnapshotAsync(
            orgId: ORG_ID,
            userPrompt: $"Rules: {OLD_VALUE_A}",
            systemPrompt: null,
            captured: new() { [FIELD_B] = "other-value" });

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, NEW_VALUE_A);

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(TestSignaturePromptCommandHandler.ERROR_PLACEHOLDER_NOT_CAPTURED, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_CapturedValueNotFoundInPrompt_ReturnsConflict()
    {
        // Captured dict says old = "foo" but prompt doesn't actually contain "foo".
        await SeedExecutionSnapshotAsync(
            orgId: ORG_ID,
            userPrompt: "Totally unrelated prompt body",
            systemPrompt: null,
            captured: new() { [FIELD_A] = "foo" });

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, "bar");

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(TestSignaturePromptCommandHandler.ERROR_OLD_VALUE_NOT_FOUND, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_MultiOccurrence_ReplacesInBothUserAndSystemPrompt()
    {
        await SeedExecutionSnapshotAsync(
            orgId: ORG_ID,
            userPrompt: $"User: {OLD_VALUE_A}",
            systemPrompt: $"System: {OLD_VALUE_A}",
            captured: new() { [FIELD_A] = OLD_VALUE_A });

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, NEW_VALUE_A);

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.OccurrencesReplaced);

        ScriptedModelCall call = Assert.Single(_chatConnector.Calls);
        Assert.NotNull(call.SystemPrompt);
        Assert.Contains(NEW_VALUE_A, call.Text);
        Assert.Contains(NEW_VALUE_A, call.SystemPrompt);
        Assert.DoesNotContain(OLD_VALUE_A, call.Text);
        Assert.DoesNotContain(OLD_VALUE_A, call.SystemPrompt);
    }

    [Fact]
    public async Task Handle_TestValueMatchesCapturedValue_ReturnsValidationError()
    {
        await SeedExecutionSnapshotAsync(
            orgId: ORG_ID,
            userPrompt: $"Rules: {OLD_VALUE_A}",
            systemPrompt: null,
            captured: new() { [FIELD_A] = OLD_VALUE_A });

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, OLD_VALUE_A);

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(TestSignaturePromptCommandHandler.ERROR_NO_CHANGE, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_ExecutionBelongsToAnotherOrg_ReturnsNotFound()
    {
        await SeedExecutionSnapshotAsync(
            orgId: OTHER_ORG_ID,
            userPrompt: $"Rules: {OLD_VALUE_A}",
            systemPrompt: null,
            captured: new() { [FIELD_A] = OLD_VALUE_A });

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, NEW_VALUE_A);

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(TestSignaturePromptCommandHandler.ERROR_EXECUTION_NOT_FOUND, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_PromptMissing_ReturnsValidation()
    {
        // Execution has a block but no captured UserPrompt.
        var root = new Workflow("Test Exec")
        {
            Id = EXECUTION_ID,
            OrganizationId = ORG_ID,
        };
        var section = new Section("Section", root) { Order = 0 };
        var block = new Block(BlockTypes.AiQuery, section)
        {
            Id = BLOCK_ID,
            Name = "AiBlock",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "x",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    [FIELD_A] = new("COGS", "", "d"),
                },
                // ExecutionState deliberately null — no captured prompt.
            },
        };
        _ = _context.Workflows.Add(root);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, NEW_VALUE_A);

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(TestSignaturePromptCommandHandler.ERROR_PROMPT_MISSING, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_TargetBlockIsMerge_ReturnsBlockNotAiQueryError()
    {
        // Defense in depth: the Vue side hides the Test button for non-AiQuery blocks
        // (gated on SignatureBlockDto.BlockType), but a direct API call against a Merge
        // block must also be rejected at the handler — we never want to attempt prompt
        // replay against a block type that has no captured prompt.
        var root = new Workflow("Test Exec")
        {
            Id = EXECUTION_ID,
            OrganizationId = ORG_ID,
        };
        var section = new Section("Section", root) { Order = 0 };
        _ = new Block(BlockTypes.Merge, section)
        {
            Id = BLOCK_ID,
            Name = "MergeBlock",
            Properties = new MergeProperties
            {
                TextTemplate = "Use {tone} please.",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    [FIELD_A] = new("Tone", "", "formal"),
                },
            },
        };
        _ = _context.Workflows.Add(root);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new TestSignaturePromptCommand(
            ORG_ID, USER_ID, EXECUTION_ID, BLOCK_ID, FIELD_A, NEW_VALUE_A);

        ErrorOr<SignaturePromptTestResultDto> result = await _handler.Handle(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(TestSignaturePromptCommandHandler.ERROR_BLOCK_NOT_AI_QUERY, result.FirstError.Code);
    }

    private async Task SeedExecutionSnapshotAsync(
        string orgId,
        string userPrompt,
        string? systemPrompt,
        Dictionary<string, string>? captured)
    {
        var root = new Workflow("Test Exec")
        {
            Id = EXECUTION_ID,
            OrganizationId = orgId,
        };
        var section = new Section("Section", root) { Order = 0 };
        var props = new AiQueryProperties
        {
            // These become the starting UserPrompt/SystemPrompt on the created ExecutionState;
            // we override them below to match the exact captured strings the test needs.
            UserPromptTemplate = userPrompt,
            SystemPromptTemplate = systemPrompt,
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                [FIELD_A] = new("Field A", "", "default-a"),
                [FIELD_B] = new("Field B", "", "default-b"),
            },
        };

        // Go through the production path to materialise ExecutionState, then stamp
        // the captured dict + prompts directly so we can exercise edge cases (pre-capture,
        // not-found, etc.) that normal execution wouldn't naturally produce.
        props.SetExecutionState(asPlaceHolder: false, inputs: []);
        props.ExecutionState!.UserPrompt = userPrompt;
        props.ExecutionState.SystemPrompt = systemPrompt;
        props.ExecutionState.ResolvedSignatureFieldValues = captured;

        var block = new Block(BlockTypes.AiQuery, section)
        {
            Id = BLOCK_ID,
            Name = "AiBlock",
            Properties = props,
        };

        _ = _context.Workflows.Add(root);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
