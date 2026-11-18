using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Workflows.Services;
using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Core.Services;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.InputRules;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Workflows.InputRules;

/// <summary>
/// A submission is refused before any run exists when it breaks a variable's input rule, and a
/// rule the workflow can no longer check is skipped with a line in the run's log.
/// </summary>
public sealed class InputRuleSubmissionTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-input-rules";
    private const string USER_ID = "user-input-rules";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly RecordingJobsService _jobs = new();

    public InputRuleSubmissionTests()
    {
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices();
        _ = services.AddTestFileStorage();
        _ = services.AddSeamDefaults();
        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.WorkflowTypes.Add(new WorkflowType(InputRuleWorkflows.WORKFLOW_TYPE_ID, "Interim report"));
        core.GrantWorkflowTypeAccess(InputRuleWorkflows.WORKFLOW_TYPE_ID, ORGANIZATION_ID);
        _ = core.SaveChanges();
        UserDataDbContext userData = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = userData.Organizations.Add(new Organization("Test organization", ORGANIZATION_ID));
        _ = userData.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    [Fact]
    public async Task AQuarterlyReportWithTheInterimPeriodLeftAtNA_IsRefusedWithTheRuleMessageAndStartsNothing()
    {
        ErrorOr<WorkflowExecutionDto> submitted = await SubmitAsync(
            InputRuleWorkflows.Interim(),
            InputRuleWorkflows.NOT_AN_INTERIM,
            InputRuleWorkflows.QUARTERLY);

        Error error = Assert.Single(submitted.Errors);
        Assert.Equal(VariableInputRuleErrors.INPUT_RULE_CODE, error.Code);
        Assert.Equal(InputRuleWorkflows.ATTACHED_BUT_NA, error.Description);
        Assert.Equal(InputRuleWorkflows.INTERIM, error.Metadata![WorkflowVariableValidationService.REPLACEMENT_TAG_METADATA_KEY]);
        Assert.Empty(_jobs.ExecutedJobs);
        Assert.Equal(0, await CountAsync<WorkflowExecution>());
    }

    [Fact]
    public async Task NoQuarterlyReportAndAnInterimPeriodOfSixMonths_IsRefusedByTheReverseRule()
    {
        ErrorOr<WorkflowExecutionDto> submitted = await SubmitAsync(
            InputRuleWorkflows.Interim(),
            InputRuleWorkflows.SIX_MONTHS,
            InputRuleWorkflows.ANNUAL);

        Assert.Equal(InputRuleWorkflows.NONE_ATTACHED_BUT_SIX, Assert.Single(submitted.Errors).Description);
    }

    [Fact]
    public async Task AQuarterlyReportWithAnInterimPeriodOfSixMonths_StartsTheRunWithNoSkippedRule()
    {
        ErrorOr<WorkflowExecutionDto> submitted = await SubmitAsync(
            InputRuleWorkflows.Interim(),
            InputRuleWorkflows.SIX_MONTHS,
            InputRuleWorkflows.QUARTERLY);

        Assert.False(submitted.IsError, string.Join("; ", submitted.ErrorsOrEmptyList.Select(error => error.Description)));
        Assert.Single(_jobs.ExecutedJobs);
        Assert.Equal(0, await CountAsync<WorkflowExecutionLog>());
    }

    [Fact]
    public async Task ARuleThatNamesAMissingDocumentClass_IsSkippedAndTheRunLogSaysWhy()
    {
        Workflow workflow = InputRuleWorkflows.Interim();
        workflow.Variables.Single().InputRules[0].Conditions[0].DocumentClassKeys = ["QuarterlyOld"];

        ErrorOr<WorkflowExecutionDto> submitted = await SubmitAsync(
            workflow,
            InputRuleWorkflows.NOT_AN_INTERIM,
            InputRuleWorkflows.QUARTERLY);

        Assert.False(submitted.IsError, string.Join("; ", submitted.ErrorsOrEmptyList.Select(error => error.Description)));
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        WorkflowExecutionLog log = await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().WorkflowExecutionLogs
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(submitted.Value.Id, log.WorkflowExecutionId);
        Assert.Equal(VariableInputRuleErrors.RUN_INPUTS_SECTION_NAME, log.SectionName);
        Assert.Equal("Interim Period", log.BlockName);
        Assert.Equal(BlockExecutionStatus.Skipped, log.Status);
        Assert.Equal(
            "The rule on \"Interim Period\" was not checked because it names the document class \"QuarterlyOld\", which this workflow does not have. Fix the rule on this field in the workflow editor.",
            log.Error);
    }

    private async Task<ErrorOr<WorkflowExecutionDto>> SubmitAsync(Workflow workflow, string interimPeriod, string attachedClass)
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.Workflows.Add(workflow);
        _ = await core.SaveChangesAsync(TestContext.Current.CancellationToken);

        UserDataDbContext userData = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var document = new Document(new FileMetadata(ORGANIZATION_ID, "Report.pdf", Encoding.UTF8.GetBytes("Report")), MediaType.ApplicationPdf);
        _ = userData.Add(document);
        _ = await userData.SaveChangesAsync(TestContext.Current.CancellationToken);

        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        var orchestrator = new WorkflowExecutionOrchestrator(
            NullLogger<WorkflowExecutionOrchestrator>.Instance,
            _jobs,
            unitOfWork,
            scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>(),
            new WorkflowCanonicalizer(),
            new WorkflowVariableValidationService(),
            new DocumentClassValidationService(NullLogger<DocumentClassValidationService>.Instance),
            new WorkflowOutputService(
                new WorkflowOutputBuilder(),
                new WorkflowOutputValidator(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<WorkflowOutputService>.Instance,
                hostBoot: new HostBootIdentity()),
            new ExecutionFreezeSourceResolver(unitOfWork, NullLogger<ExecutionFreezeSourceResolver>.Instance),
            new NoCaseFreezeSources(),
            access: scope.ServiceProvider.GetRequiredService<WorkflowTypeAccess>());

        return await orchestrator.ExecuteWorkflowAsync(
            workflow,
            [new PostDocumentInfoRequest { FileId = document.Id, DocumentClass = attachedClass }],
            new Dictionary<string, JsonElement> { [InputRuleWorkflows.INTERIM] = JsonSerializer.SerializeToElement(interimPeriod) },
            ORGANIZATION_ID,
            USER_ID,
            username: "analyst",
            email: null,
            counterpartyId: null,
            peerCompanyIds: null,
            ct: TestContext.Current.CancellationToken);
    }

    private async Task<int> CountAsync<TEntity>()
        where TEntity : class
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserDataDbContext>().Set<TEntity>().CountAsync(TestContext.Current.CancellationToken);
    }
}
