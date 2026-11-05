using Ardalis.Specification;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Workflows.Interfaces;
using IniaAi.Application.Workflows.Services;
using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Documents;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.DocumentClassValidation;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Replay;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Models.Jobs;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.Shared;

public class WorkflowExecutionOrchestratorTests
{
    private readonly IJobsService _jobsService;
    private readonly IWorkflowOutputService _workflowOutputService;
    private readonly IExecutionFreezeSourceResolver _freezeSourceResolver;
    private readonly ICaseFreezeSources _caseFreezeSources = Substitute.For<ICaseFreezeSources>();
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IWorkflowActivityScriptEnsurer _activityScriptEnsurer;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly WorkflowExecution _createdExecution = new();
    private readonly WorkflowExecutionOrchestrator _orchestrator;
    private readonly IWorkflowVariableValidationService _variableValidationService;
    private readonly IDocumentClassValidationService _documentClassValidationService;
    private Job? _executedJob;

    public WorkflowExecutionOrchestratorTests()
    {
        _jobsService = Substitute.For<IJobsService>();
        _userDataUnitOfWork = Substitute.For<IUserDataUnitOfWork>();
        _ = _userDataUnitOfWork
            .InTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<ErrorOr<(Job, WorkflowExecution)>>>>(),
                Arg.Any<Func<ErrorOr<(Job, WorkflowExecution)>, bool>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<ErrorOr<(Job, WorkflowExecution)>>>>()(CancellationToken.None));
        ICoreUnitOfWork coreUnitOfWork = Substitute.For<ICoreUnitOfWork>();
        _coreUnitOfWork = coreUnitOfWork;
        GrantAccess(new WorkflowTypeOrganizationAccess { WorkflowTypeId = "wf-type-1", OrganizationId = "org-1" });
        _activityScriptEnsurer = Substitute.For<IWorkflowActivityScriptEnsurer>();
        IWorkflowVariableValidationService variableValidationService =
            Substitute.For<IWorkflowVariableValidationService>();
        _variableValidationService = variableValidationService;
        IDocumentClassValidationService documentClassValidationService =
            Substitute.For<IDocumentClassValidationService>();
        _documentClassValidationService = documentClassValidationService;
        _workflowOutputService = Substitute.For<IWorkflowOutputService>();

        _ = variableValidationService
            .ValidateVariables(Arg.Any<Workflow>(), Arg.Any<Dictionary<string, JsonElement>?>())
            .Returns([]);
        _ = variableValidationService
            .ConvertVariablesToStrings(Arg.Any<Dictionary<string, JsonElement>?>())
            .Returns([]);
        _ = variableValidationService
            .ConvertVariablesToObjects(Arg.Any<Dictionary<string, JsonElement>?>())
            .Returns([]);

        _ = documentClassValidationService
            .ValidateDocumentClasses(Arg.Any<Workflow>(), Arg.Any<List<Document>>())
            .Returns(new DocumentClassValidationResult { IsValid = true });

        _ = _jobsService.CreateUserJob(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(_ => new Job("org-1", "wf-type-1"));
        _jobsService.When(s => s.Execute(Arg.Any<Job>(), Arg.Any<bool>()))
            .Do(callInfo => _executedJob = callInfo.Arg<Job>());

        _ = _workflowOutputService.CreateExecutionFromWorkflowAsync(
                Arg.Any<Workflow>(),
                Arg.Any<WorkflowOutputContext?>(),
                Arg.Any<IUserDataUnitOfWork>(),
                Arg.Any<DateTime?>(),
                Arg.Any<WorkflowState?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, object>?>())
            .Returns(_createdExecution);

        _freezeSourceResolver = Substitute.For<IExecutionFreezeSourceResolver>();

        _orchestrator = CreateOrchestrator(agentRuntimeEnabled: false);
    }

    private const string FOUNDRY_SONNET = "claude-sonnet-5";
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5";
    private const string BEDROCK_OPUS = "us.anthropic.claude-opus-5";

    private WorkflowExecutionOrchestrator CreateOrchestrator(bool agentRuntimeEnabled, params string[] allowedModels)
    {
        return CreateOrchestrator(agentRuntimeEnabled, _variableValidationService, registrations: null, allowedModels);
    }

    private WorkflowExecutionOrchestrator CreateOrchestrator(
        bool agentRuntimeEnabled,
        IWorkflowVariableValidationService variableValidationService,
        IServiceProviderIsService? registrations,
        params string[] allowedModels)
    {
        return new WorkflowExecutionOrchestrator(
            Substitute.For<ILogger<WorkflowExecutionOrchestrator>>(),
            _jobsService,
            _userDataUnitOfWork,
            _coreUnitOfWork,
            new WorkflowCanonicalizer(),
            variableValidationService,
            _documentClassValidationService,
            _workflowOutputService,
            _freezeSourceResolver,
            _caseFreezeSources,
            agentRuntimeSettings: Options.Create(new AgentRuntimeSettings
            {
                Enabled = agentRuntimeEnabled,
                AllowedModels = [.. allowedModels],
            }),
            activityScriptEnsurer: _activityScriptEnsurer,
            registrations: registrations);
    }

    /// <summary>
    /// The verification loop shipped whole (batch B, 2026-08-28 → 2026-09-01), and the
    /// pre-spend submission refusal of verification blocks lifted with it: a workflow that
    /// carries one submits like any other on an enabled runtime. The runtime-disabled gate
    /// still covers the whole agent family, so a disabled environment refuses before any spend.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_WithAVerificationBlockOnAnEnabledRuntime_Submits()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            workflow: AgentFamilyWorkflow(BlockTypes.Verification, FOUNDRY_SONNET),
            orchestrator: CreateOrchestrator(agentRuntimeEnabled: true, FOUNDRY_SONNET));

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        await AssertExecutionCreatedAsync();
    }

    /// <summary>
    /// On an Azure stack the allowlist carries Foundry deployment names; the submit gate holds
    /// every agent-family block to it so a template saved against an older list is refused
    /// before the upstream blocks spend, instead of failing at the runtime's 400 after they have.
    /// </summary>
    [Theory]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    public async Task ExecuteWorkflowAsync_FoundryDeploymentNameInTheAllowlist_Submits(BlockTypes blockType)
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            workflow: AgentFamilyWorkflow(blockType, FOUNDRY_SONNET),
            orchestrator: CreateOrchestrator(agentRuntimeEnabled: true, FOUNDRY_SONNET));

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        await AssertExecutionCreatedAsync();
    }

    [Theory]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    public async Task ExecuteWorkflowAsync_BedrockIdOfAnAllowedModel_SubmitsAgainstAFoundryAllowlist(
        BlockTypes blockType)
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            workflow: AgentFamilyWorkflow(blockType, BEDROCK_SONNET),
            orchestrator: CreateOrchestrator(agentRuntimeEnabled: true, FOUNDRY_SONNET));

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        await AssertExecutionCreatedAsync();
    }

    [Theory]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    public async Task ExecuteWorkflowAsync_ModelNotAllowedInAnyForm_RefusesWithoutCreatingAnExecution(
        BlockTypes blockType)
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            workflow: AgentFamilyWorkflow(blockType, BEDROCK_OPUS),
            orchestrator: CreateOrchestrator(agentRuntimeEnabled: true, FOUNDRY_SONNET));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(WorkflowExecutionOrchestrator.AGENT_MODEL_NOT_ALLOWED_CODE, result.FirstError.Code);
        Assert.Contains(BEDROCK_OPUS, result.FirstError.Description, StringComparison.Ordinal);
        Assert.Contains(FOUNDRY_SONNET, result.FirstError.Description, StringComparison.Ordinal);
        Assert.Null(_executedJob);
        await AssertNoExecutionCreatedAsync();
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_AWorkflowTypeTheOrganizationCannotReach_AnswersNotFoundAndStartsNothing()
    {
        GrantAccess(null);

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Workflow not found.", result.FirstError.Description);
        Assert.Null(_executedJob);
        await AssertNoExecutionCreatedAsync();
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_AConfidentialRun_WhenNoAnonymiserIsRegistered_RefusesWithoutCreatingAnExecution()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, isConfidential: true);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(WorkflowExecutionOrchestrator.ANONYMISATION_UNAVAILABLE_CODE, result.FirstError.Code);
        Assert.Equal("The platform cannot anonymise documents, so it cannot start a confidential run. Start the run with isConfidential set to false. The full edition anonymises documents before any model reads them.", result.FirstError.Description);
        Assert.Null(_executedJob);
        await AssertNoExecutionCreatedAsync();
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_AConfidentialRun_WhenAnAnonymiserIsRegistered_StartsAConfidentialJob()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddTransient<IConfidentialDocumentAnonymiser>(_ => throw new InvalidOperationException("The orchestrator only asks whether an anonymiser is registered."))
            .BuildServiceProvider();

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            orchestrator: CreateOrchestrator(agentRuntimeEnabled: false, variableValidationService: _variableValidationService, registrations: services.GetRequiredService<IServiceProviderIsService>()),
            isConfidential: true);

        Assert.False(result.IsError);
        Assert.True(_executedJob?.IsConfidential);
        await AssertExecutionCreatedAsync();
    }

    private Task AssertExecutionCreatedAsync()
    {
        return _workflowOutputService.ReceivedWithAnyArgs(1).CreateExecutionFromWorkflowAsync(
            default!, default, default!, default, default, default, default);
    }

    private void GrantAccess(WorkflowTypeOrganizationAccess? access)
    {
        _ = _coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>()
            .GetFirstOrDefaultAsync(Arg.Any<ISpecification<WorkflowTypeOrganizationAccess>>(), Arg.Any<CancellationToken>())
            .Returns(access);
    }

    private Task AssertNoExecutionCreatedAsync()
    {
        return _workflowOutputService.DidNotReceiveWithAnyArgs().CreateExecutionFromWorkflowAsync(
            default!, default, default!, default, default, default, default);
    }

    private void ArrangeHeadVersion(WorkflowVersion? headVersion)
    {
        IRepository<WorkflowVersion> versionRepository = Substitute.For<IRepository<WorkflowVersion>>();
        _ = versionRepository
            .GetFirstOrDefaultAsync(Arg.Any<ISpecification<WorkflowVersion>>(), Arg.Any<CancellationToken>())
            .Returns(headVersion);
        _ = _coreUnitOfWork.Repository<WorkflowVersion>().Returns(versionRepository);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithAHeadVersion_AsksForItsActivityScriptInTheBackground()
    {
        ArrangeHeadVersion(new WorkflowVersion
        {
            Id = "version-1",
            WorkflowTypeId = "wf-type-1",
            ContentHash = new string('a', 64),
        });

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null);

        Assert.False(result.IsError);
        _activityScriptEnsurer.Received(1).EnsureScriptInBackground("version-1", "wf-type-1");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithoutAHeadVersion_NeverAsksForAnActivityScript()
    {
        ArrangeHeadVersion(null);

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null);

        Assert.False(result.IsError);
        _activityScriptEnsurer.DidNotReceiveWithAnyArgs().EnsureScriptInBackground(default!, default!);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_MissingRequiredVariables_RefusesNamingEachVariable()
    {
        var workflow = new Workflow { Id = "wf-1", WorkflowTypeId = "wf-type-1" };
        _ = new WorkflowVariable(workflow, "qa_required_note") { DisplayName = "Note", IsRequired = true };
        _ = new WorkflowVariable(workflow, "as_of") { DisplayName = "As of", IsRequired = true };
        WorkflowExecutionOrchestrator orchestrator = CreateOrchestrator(
            agentRuntimeEnabled: false,
            new WorkflowVariableValidationService(),
            registrations: null);

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, workflow: workflow, orchestrator: orchestrator);

        Assert.True(result.IsError);
        Assert.All(result.Errors, error => Assert.Equal("WorkflowVariable.Required", error.Code));
        Assert.Equal(
            ["Required variable 'Note' (qa_required_note) is missing.", "Required variable 'As of' (as_of) is missing."],
            result.Errors.Select(error => error.Description));
        Assert.Null(_executedJob);
    }

    private async Task<ErrorOr<WorkflowExecutionDto>> ExecuteAsync(
        string? triggerEmailIdentifier,
        IReadOnlyList<SkippedProtectedFile>? intakeSkippedFiles,
        string? emailIntakeMode = null,
        string? emailAttachmentsClass = null,
        Workflow? workflow = null,
        FreezeSource? freezeSource = null,
        List<PostDocumentInfoRequest>? files = null,
        WorkflowExecutionOrchestrator? orchestrator = null,
        bool isRerun = false,
        bool isConfidential = false)
    {
        return await (orchestrator ?? _orchestrator).ExecuteWorkflowAsync(
            workflow ?? new Workflow { Id = "wf-1", WorkflowTypeId = "wf-type-1" },
            files: files,
            variables: null,
            organizationId: "org-1",
            userId: "user-1",
            username: "test",
            email: "test@test.com",
            counterpartyId: null,
            peerCompanyIds: null,
            isConfidential: isConfidential,
            freezeSource: freezeSource,
            triggerEmailIdentifier: triggerEmailIdentifier,
            intakeSkippedFiles: intakeSkippedFiles,
            emailIntakeMode: emailIntakeMode,
            emailAttachmentsClass: emailAttachmentsClass,
            isRerun: isRerun);
    }

    private static List<ReplayOutput> Captures()
    {
        return [new("Fetches", "api", "WebApiCall", "captured", null, "stable-api")];
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithFreezeSource_ReplaysLiveAndStampsProvenance()
    {
        List<ReplayOutput> captures = Captures();
        _ = _freezeSourceResolver
            .ResolveAsync("org-1", "exec-source", Arg.Any<Workflow>(), null, null, Arg.Any<CancellationToken>())
            .Returns(ErrorOrFactory.From<IReadOnlyList<ReplayOutput>>(captures));

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, freezeSource: new FreezeSource.Execution("exec-source"));

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Same(captures, _executedJob!.ReplayOutputs);
        Assert.Equal(ReplayUncoveredBlockPolicy.RunLive, _executedJob.ReplayUncoveredBlockPolicy);
        Assert.Equal("exec-source", _createdExecution.FreezeSourceExecutionId);
        Assert.Null(_createdExecution.FreezeSourceCaseId);
        Assert.True(_executedJob.IsRerun);
    }

    /// <summary>
    /// A case resolves through its seam, and the orchestrator stamps what it
    /// resolved: the case this deployment holds, and the execution that produced the captures —
    /// which for an imported case belongs to another deployment.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_WithACaseFreezeSource_StampsTheCaseAndTheSourceExecution()
    {
        List<ReplayOutput> captures = Captures();
        FreezeSource.Case source = new("case-1");
        _ = _caseFreezeSources
            .ResolveAsync(source, "org-1", Arg.Any<Workflow>(), null, null, Arg.Any<CancellationToken>())
            .Returns(new ResolvedFreeze(captures, "exec-on-the-exporter", "case-1", ReplayUncoveredBlockPolicy.RunLive));

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, freezeSource: source);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Same(captures, _executedJob!.ReplayOutputs);
        Assert.Equal(ReplayUncoveredBlockPolicy.RunLive, _executedJob.ReplayUncoveredBlockPolicy);
        Assert.Equal("case-1", _createdExecution.FreezeSourceCaseId);
        Assert.Equal("exec-on-the-exporter", _createdExecution.FreezeSourceExecutionId);
    }

    /// <summary>
    /// An imported case whose bundle carried no baseline execution still freezes: the case alone
    /// carries the provenance, and the run is frozen because the case id is stamped.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_WithACaseThatNamesNoSourceExecution_StampsTheCaseAlone()
    {
        List<ReplayOutput> captures = Captures();
        FreezeSource.Case source = new("case-1");
        _ = _caseFreezeSources
            .ResolveAsync(source, "org-1", Arg.Any<Workflow>(), null, null, Arg.Any<CancellationToken>())
            .Returns(new ResolvedFreeze(captures, null, "case-1", ReplayUncoveredBlockPolicy.RunLive));

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, freezeSource: source);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Same(captures, _executedJob!.ReplayOutputs);
        Assert.Equal(ReplayUncoveredBlockPolicy.RunLive, _executedJob.ReplayUncoveredBlockPolicy);
        Assert.Equal("case-1", _createdExecution.FreezeSourceCaseId);
        Assert.Null(_createdExecution.FreezeSourceExecutionId);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_FreezeSourceGuardFails_ReturnsTheErrorAndStartsNothing()
    {
        Error blocked = Error.Validation("WorkflowExecution.FreezeSourceVariablesMismatch", "Year");
        _ = _freezeSourceResolver
            .ResolveAsync("org-1", "exec-source", Arg.Any<Workflow>(), null, null, Arg.Any<CancellationToken>())
            .Returns(blocked);

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, freezeSource: new FreezeSource.Execution("exec-source"));

        Assert.True(result.IsError);
        Assert.Equal(blocked.Code, result.FirstError.Code);
        Assert.Null(_executedJob);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_DormantReplayOutputs_KeepSkipAsInconclusiveAndNoProvenance()
    {
        List<ReplayOutput> captures = [];

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, freezeSource: new FreezeSource.ReplaySet(captures));

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Same(captures, _executedJob!.ReplayOutputs);
        Assert.Equal(ReplayUncoveredBlockPolicy.SkipAsInconclusive, _executedJob.ReplayUncoveredBlockPolicy);
        Assert.Null(_createdExecution.FreezeSourceExecutionId);
        await _freezeSourceResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithoutAFreezeSource_ReplaysNothing()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Null(_executedJob!.ReplayOutputs);
        Assert.Equal(ReplayUncoveredBlockPolicy.SkipAsInconclusive, _executedJob.ReplayUncoveredBlockPolicy);
        Assert.Null(_createdExecution.FreezeSourceExecutionId);
        Assert.Null(_createdExecution.FreezeSourceCaseId);
        Assert.False(_executedJob.IsRerun);
        await _freezeSourceResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A validation act or a regression rerun says so, and the job and its execution carry the mark
    /// even when nothing is frozen: such a run saves no change to live data.
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowAsync_ARerunWithoutAFreezeSource_MarksTheJobAndTheExecution()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, isRerun: true);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.True(_executedJob!.IsRerun);
        Assert.Null(_executedJob.ReplayOutputs);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_TheRegressionReplaySet_IsARerun()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null, freezeSource: new FreezeSource.ReplaySet([]));

        Assert.False(result.IsError);
        Assert.True(_executedJob!.IsRerun);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_UnresolvableFileId_RejectsNamingTheDeadIdAndStartsNothing()
    {
        Document liveDocument = NewDocument("Live.pdf");
        ResolveDocuments(liveDocument);

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            triggerEmailIdentifier: null,
            intakeSkippedFiles: null,
            files:
            [
                new PostDocumentInfoRequest { FileId = liveDocument.Id },
                new PostDocumentInfoRequest { FileId = "doc-dead" },
            ]);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(WorkflowExecutionOrchestrator.UNRESOLVABLE_INPUT_DOCUMENTS_CODE, result.FirstError.Code);
        Assert.Contains("'doc-dead'", result.FirstError.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(liveDocument.Id, result.FirstError.Description, StringComparison.Ordinal);
        Assert.Null(_executedJob);
        await AssertNoExecutionCreatedAsync();
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_EveryFileIdResolves_Continues()
    {
        Document firstDocument = NewDocument("First.pdf");
        Document secondDocument = NewDocument("Second.pdf");
        ResolveDocuments(firstDocument, secondDocument);

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            triggerEmailIdentifier: null,
            intakeSkippedFiles: null,
            files:
            [
                new PostDocumentInfoRequest { FileId = firstDocument.Id },
                new PostDocumentInfoRequest { FileId = secondDocument.Id },
            ]);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_UnscreenedType_StillRefusesAnUnsatisfiedClassBeforeStarting()
    {
        ArrangeUnsatisfiedDocumentClasses();

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null);

        Assert.True(result.IsError);
        Assert.Equal("DocumentClasses", result.FirstError.Code);
        Assert.Equal("Required document class 'Statement' is missing", result.FirstError.Description);
        Assert.Null(_executedJob);
    }

    private void ArrangeUnsatisfiedDocumentClasses(params DocumentClassStatus[] moreUnsatisfied)
    {
        DocumentClassStatus missingStatement = new()
        {
            Key = "Statement",
            DisplayName = "Statement",
            Requirement = "Required",
            IsSatisfied = false,
            ValidationMessage = "Required document class 'Statement' is missing",
        };

        _ = _documentClassValidationService
            .ValidateDocumentClasses(Arg.Any<Workflow>(), Arg.Any<List<Document>>())
            .Returns(new DocumentClassValidationResult
            {
                IsValid = false,
                Errors = [missingStatement.ValidationMessage!, .. moreUnsatisfied.Select(status => status.ValidationMessage!)],
                DocumentClassStatuses = [missingStatement, .. moreUnsatisfied],
            });
    }

    private static Document NewDocument(string name)
    {
        return new Document(new FileMetadata("org-1", name, 123, StorageProvider.Database, Guid.NewGuid().ToString()), Utils.DetermineMediaTypeFromExtension(Path.GetExtension(name)));
    }

    private void ResolveDocuments(params Document[] documents)
    {
        _ = _userDataUnitOfWork.Documents
            .GetDocumentsWithInfoAsync(
                Arg.Any<List<string>>(),
                "org-1",
                Arg.Any<IEnumerable<(string, string, bool, bool)>>())
            .Returns([.. documents]);
    }

    [Theory]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    public async Task ExecuteWorkflowAsync_AgentFamilyWorkflowOnDisabledRuntime_RefusesWithoutCreatingAnExecution(
        BlockTypes blockType)
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            triggerEmailIdentifier: null,
            intakeSkippedFiles: null,
            workflow: AgentFamilyWorkflow(blockType));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(WorkflowExecutionOrchestrator.AGENT_RUNTIME_DISABLED_CODE, result.FirstError.Code);
        Assert.Contains("agent runtime is disabled", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Null(_executedJob);
        await AssertNoExecutionCreatedAsync();
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_PlainWorkflowOnDisabledRuntime_IsUnaffected()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            triggerEmailIdentifier: null,
            intakeSkippedFiles: null);

        Assert.False(result.IsError);
    }

    private static Workflow AgentFamilyWorkflow(BlockTypes blockType, string? model = null)
    {
        var workflow = new Workflow("AgentWf") { Id = "wf-agent", WorkflowTypeId = "wf-type-agent" };
        var section = new Section("Body", workflow);
        var block = new Block(blockType, section, "familyBlock");
        if (block.Properties is IAgentRunProperties props)
        {
            props.Model = model;
        }

        return workflow;
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithIdentifierAndSkips_WritesBothJobInputs()
    {
        List<SkippedProtectedFile> skips = [new("notes.docx", "unsupported type")];

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync("Monthly_NAV_2026-05-04_09-30-15", skips);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Equal(
            "Monthly_NAV_2026-05-04_09-30-15",
            _executedJob!.Inputs[SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER]);

        List<SkippedProtectedFile> roundTripped = SkippedProtectedFile.Deserialize(
            _executedJob.Inputs[IntakeInputKeys.SKIPPED_PROTECTED_FILES]);
        SkippedProtectedFile skip = Assert.Single(roundTripped);
        Assert.Equal("notes.docx", skip.FileName);
        Assert.Equal("unsupported type", skip.Reason);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithoutIdentifierAndSkips_WritesNeitherJobInput()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.False(_executedJob!.Inputs.ContainsKey(SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER));
        Assert.False(_executedJob.Inputs.ContainsKey(IntakeInputKeys.SKIPPED_PROTECTED_FILES));
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithEmailIntakeMode_WritesModeAndClassJobInputs()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            emailIntakeMode: EmailIntakeModes.EXTRACT_ATTACHMENTS,
            emailAttachmentsClass: "Attachment");

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Equal(
            EmailIntakeModes.EXTRACT_ATTACHMENTS,
            _executedJob!.Inputs[IntakeInputKeys.EMAIL_INTAKE_MODE]);
        Assert.Equal("Attachment", _executedJob.Inputs[IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS]);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_PdfOnlyModeWithoutClass_WritesModeOnly()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            emailIntakeMode: EmailIntakeModes.PDF_ONLY);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Equal(EmailIntakeModes.PDF_ONLY, _executedJob!.Inputs[IntakeInputKeys.EMAIL_INTAKE_MODE]);
        Assert.False(_executedJob.Inputs.ContainsKey(IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS));
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_AttachmentsClassIsOutputClass_ReturnsValidationError()
    {
        var workflow = new Workflow { Id = "wf-1", WorkflowTypeId = "wf-type-1" };
        _ = new DocumentClass(workflow, "Financial Spread Output") { IsOutputDocument = true };

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            emailIntakeMode: EmailIntakeModes.EXTRACT_ATTACHMENTS,
            emailAttachmentsClass: "Financial Spread Output",
            workflow: workflow);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Null(_executedJob);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_AttachmentsClassIsInputClass_Succeeds()
    {
        var workflow = new Workflow { Id = "wf-1", WorkflowTypeId = "wf-type-1" };
        _ = new DocumentClass(workflow, "Attachment") { IsOutputDocument = false };

        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(
            null,
            null,
            emailIntakeMode: EmailIntakeModes.EXTRACT_ATTACHMENTS,
            emailAttachmentsClass: "Attachment",
            workflow: workflow);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.Equal("Attachment", _executedJob!.Inputs[IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS]);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithoutEmailIntakeMode_WritesNoIntakeKeys()
    {
        ErrorOr<WorkflowExecutionDto> result = await ExecuteAsync(null, null);

        Assert.False(result.IsError);
        Assert.NotNull(_executedJob);
        Assert.False(_executedJob!.Inputs.ContainsKey(IntakeInputKeys.EMAIL_INTAKE_MODE));
        Assert.False(_executedJob.Inputs.ContainsKey(IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS));
    }
}
