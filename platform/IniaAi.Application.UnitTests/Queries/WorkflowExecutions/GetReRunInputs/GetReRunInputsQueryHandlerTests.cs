using System.Text;
using ErrorOr;
using IniaAi.Application.Queries.WorkflowExecutions.GetReRunInputs;
using IniaAi.Application.Workflows.Services;
using IniaAi.Common.Enums;
using IniaAi.Core.Services;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.WorkflowExecutions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetReRunInputs;

/// <summary>
/// The re-run check against a real database: the run's values and input documents come from
/// UserData, the current template and the organization's access to it from Core.
/// </summary>
public class GetReRunInputsQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";
    private const string WORKFLOW_TYPE_ID = "bank-account-analysis";
    private const string JOB_ID = "job-rerun";
    private const string STATEMENT_CLASS = "BankStatement";
    private const string REPORT_CLASS = "Report";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;
    private readonly GetReRunInputsQueryHandler _handler;

    public GetReRunInputsQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _userData = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _core = _serviceProvider.GetRequiredService<CoreDbContext>();

        _handler = new GetReRunInputsQueryHandler(
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            _serviceProvider.GetRequiredService<ICoreUnitOfWork>(),
            new WorkflowVariableValidationService(),
            new DocumentClassValidationService(NullLogger<DocumentClassValidationService>.Instance),
            NullLogger<GetReRunInputsQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_PlansTheRunAgainstTheCurrentTemplate()
    {
        Workflow template = await SeedTemplateAsync(withAccountHolder: true, grantAccess: true);
        WorkflowExecution execution = await SeedRunAsync();

        ErrorOr<ReRunInputsDto> result = await HandleAsync(execution.Id, ORG_ID);

        Assert.False(result.IsError);
        ReRunInputsDto plan = result.Value;
        Assert.Equal(template.Id, plan.WorkflowId);
        Assert.Equal(WORKFLOW_TYPE_ID, plan.WorkflowTypeId);
        Assert.False(plan.IsReady);
        Assert.Equal("account_holder_type", Assert.Single(plan.VariablesNeedingInput).ReplacementTag);
        Assert.Equal("CompanyName", Assert.Single(plan.Variables).ReplacementTag);
        Assert.Equal("Region", Assert.Single(plan.RemovedVariables).ReplacementTag);
        Assert.Empty(plan.DocumentClassesNeedingInput);
    }

    [Fact]
    public async Task Handle_CountsOnlyTheRunsInputDocuments_AgainstTheTemplatesDocumentClasses()
    {
        _ = await SeedTemplateAsync(withAccountHolder: false, grantAccess: true, statementRequired: true);
        WorkflowExecution execution = await SeedRunAsync(statementExcluded: true);

        ErrorOr<ReRunInputsDto> result = await HandleAsync(execution.Id, ORG_ID);

        Assert.False(result.IsError);
        Assert.False(result.Value.IsReady);
        Assert.Equal(STATEMENT_CLASS, Assert.Single(result.Value.DocumentClassesNeedingInput).Key);
    }

    [Fact]
    public async Task Handle_ADeletedInputDocument_IsNamedAsUnavailable_AndTheRunIsNotReady()
    {
        _ = await SeedTemplateAsync(withAccountHolder: false, grantAccess: true, statementRequired: true);
        WorkflowExecution execution = await SeedRunAsync(statementDeleted: true);

        ErrorOr<ReRunInputsDto> result = await HandleAsync(execution.Id, ORG_ID);

        Assert.False(result.IsError);
        Assert.False(result.Value.IsReady);
        Assert.Equal("statement.pdf", Assert.Single(result.Value.UnavailableDocuments).FileName);
        Assert.Equal(STATEMENT_CLASS, Assert.Single(result.Value.DocumentClassesNeedingInput).Key);
    }

    [Fact]
    public async Task Handle_WhenTheOrganizationHasNoAccessToTheType_ReturnsNotFound()
    {
        _ = await SeedTemplateAsync(withAccountHolder: true, grantAccess: false);
        WorkflowExecution execution = await SeedRunAsync();

        ErrorOr<ReRunInputsDto> result = await HandleAsync(execution.Id, ORG_ID);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("WorkflowExecution.ReRunWorkflowNotAvailable", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenTheTypeHasNoTemplate_ReturnsNotFound()
    {
        _ = _core.WorkflowTypeOrganizationAccess.Add(new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            OrganizationId = ORG_ID,
        });
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkflowExecution execution = await SeedRunAsync();

        ErrorOr<ReRunInputsDto> result = await HandleAsync(execution.Id, ORG_ID);

        Assert.True(result.IsError);
        Assert.Equal("WorkflowExecution.ReRunWorkflowNotAvailable", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_AnotherOrganizationsExecution_IsNotFound()
    {
        _ = await SeedTemplateAsync(withAccountHolder: true, grantAccess: true);
        WorkflowExecution execution = await SeedRunAsync();

        ErrorOr<ReRunInputsDto> result = await HandleAsync(execution.Id, OTHER_ORG_ID);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private Task<ErrorOr<ReRunInputsDto>> HandleAsync(string executionId, string organizationId)
    {
        return _handler.Handle(
            new GetReRunInputsQuery(organizationId, USER_ID, executionId),
            TestContext.Current.CancellationToken);
    }

    private async Task<Workflow> SeedTemplateAsync(bool withAccountHolder, bool grantAccess, bool statementRequired = false)
    {
        var template = new Workflow { WorkflowTypeId = WORKFLOW_TYPE_ID };
        _ = new WorkflowVariable(template, "CompanyName") { DisplayName = "Company name", IsRequired = true };
        if (withAccountHolder)
        {
            _ = new WorkflowVariable(template, "account_holder_type")
            {
                DisplayName = "Account Holder",
                Type = WorkflowVariableTypes.Dropdown,
                IsRequired = true,
                Options = new Dictionary<string, string> { ["Individual"] = "Individual", ["Company"] = "Company" },
            };
        }

        _ = new DocumentClass(template, STATEMENT_CLASS)
        {
            Requirement = statementRequired ? DocumentClassRequirement.Required : DocumentClassRequirement.Optional,
        };
        _ = new DocumentClass(template, REPORT_CLASS) { IsOutputDocument = true };

        _ = _core.Workflows.Add(template);
        if (grantAccess)
        {
            _ = _core.WorkflowTypeOrganizationAccess.Add(new WorkflowTypeOrganizationAccess
            {
                WorkflowTypeId = WORKFLOW_TYPE_ID,
                OrganizationId = ORG_ID,
            });
        }

        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);
        _core.ChangeTracker.Clear();
        return template;
    }

    private async Task<WorkflowExecution> SeedRunAsync(bool statementExcluded = false, bool statementDeleted = false)
    {
        var runWorkflow = new Workflow { WorkflowTypeId = WORKFLOW_TYPE_ID };
        _ = new WorkflowVariable(runWorkflow, "CompanyName") { DisplayName = "Company name", IsRequired = true };
        _ = new WorkflowVariable(runWorkflow, "Region") { DisplayName = "Region", IsRequired = false };
        _ = new DocumentClass(runWorkflow, STATEMENT_CLASS);
        _ = new DocumentClass(runWorkflow, REPORT_CLASS) { IsOutputDocument = true };
        _ = _userData.Workflows.Add(runWorkflow);

        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            WorkflowId = runWorkflow.Id,
            JobId = JOB_ID,
            Variables = new Dictionary<string, object> { ["CompanyName"] = "Acme Ltd", ["Region"] = "EMEA" },
        };
        _ = _userData.WorkflowExecutions.Add(execution);

        AddDocumentInfo("statement.pdf", STATEMENT_CLASS, statementExcluded, statementDeleted);
        AddDocumentInfo("report.xlsx", REPORT_CLASS, excluded: false);

        _ = await _userData.SaveChangesAsync(TestContext.Current.CancellationToken);
        _userData.ChangeTracker.Clear();
        return execution;
    }

    private void AddDocumentInfo(string fileName, string documentClass, bool excluded, bool deleted = false)
    {
        var document = new Document(new FileMetadata(ORG_ID, fileName, Encoding.UTF8.GetBytes(fileName)), MediaType.ApplicationPdf)
        {
            IsDeleted = deleted,
        };
        var documentInfo = new DocumentInfo(document.Id) { Document = document, JobId = JOB_ID, DocumentClass = documentClass };
        if (excluded)
        {
            documentInfo.ExcludeFromRun("NotExpectedDocument", DateTime.UtcNow);
        }

        _ = _userData.Set<DocumentInfo>().Add(documentInfo);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
