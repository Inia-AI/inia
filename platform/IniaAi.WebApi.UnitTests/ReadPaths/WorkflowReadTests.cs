using ErrorOr;
using IniaAi.Application.Queries.WorkflowExecutions;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Application.Queries.Workflows.ValidateDocumentClasses;
using IniaAi.Common.Enums;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Models.Dto;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.UnitTests.ReadPaths;

public class WorkflowReadTests : ReadPathTests
{
    private const string ORGANIZATION_ID = "org-workflow-read";
    private const string USER_ID = "user-workflow-read";
    private const string WORKFLOW_TYPE_ID = "CorpRead";

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        _ = services.AddSingleton<IDocumentClassValidationService, DocumentClassValidationService>();
    }

    [Fact]
    public async Task TheWorkflowCategories_ReadAsTheSameJson()
    {
        await SeedCategoriesAsync();

        ErrorOr<List<WorkflowCategoryDto>> result = await Handler<ListWorkflowCategoriesQueryHandler>().Handle(
            new ListWorkflowCategoriesQuery(ORGANIZATION_ID, USER_ID, IncludeWorkflows: true),
            TestContext.Current.CancellationToken);

        AssertMatchesFixture(Sorted(result.Value), "WorkflowCategories");
    }

    [Fact]
    public async Task TheWorkflowCategoriesWithoutWorkflows_ReadAsTheSameJson()
    {
        await SeedCategoriesAsync();

        ErrorOr<List<WorkflowCategoryDto>> result = await Handler<ListWorkflowCategoriesQueryHandler>().Handle(
            new ListWorkflowCategoriesQuery(ORGANIZATION_ID, USER_ID),
            TestContext.Current.CancellationToken);

        AssertMatchesFixture(Sorted(result.Value), "WorkflowCategoriesWithoutWorkflows");
    }

    [Fact]
    public async Task ADocumentClassValidation_ReadsAsTheSameJson()
    {
        Workflow workflow = BuildWorkflow("workflow-validated", WORKFLOW_TYPE_ID);
        _ = Core.WorkflowTypes.Add(new WorkflowType(WORKFLOW_TYPE_ID, "Corporate Read"));
        _ = Core.Workflows.Add(workflow);
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();
        (string Id, string Class)[] documents = [("document-annual-1", "AnnualReport"), ("document-annual-2", "AnnualReport"), ("document-other", "Brochure")];
        foreach ((string id, string documentClass) in documents)
        {
            _ = UserData.Documents.Add(new Document(new FileMetadata(ORGANIZATION_ID, $"{id}.pdf", 100, StorageProvider.Database), MediaType.ApplicationPdf)
            {
                Id = id,
                OrganizationId = ORGANIZATION_ID,
                Class = documentClass,
            });
        }

        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();

        ErrorOr<DocumentClassValidationResultDto> result = await Handler<ValidateDocumentClassesQueryHandler>().Handle(
            new ValidateDocumentClassesQuery(ORGANIZATION_ID, USER_ID, workflow.Id, [.. documents.Select(document => document.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        AssertMatchesFixture(result.Value, "DocumentClassValidation");
    }

    [Fact]
    public void AStartedExecution_AnswersWithTheSameJson()
    {
        var execution = new WorkflowExecution
        {
            Id = "execution-started",
            WorkflowId = "workflow-started",
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            Title = "Annual review",
            UserId = USER_ID,
            OrganizationId = ORGANIZATION_ID,
            ReviewDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            AnalystName = "Ada Lovelace",
            CounterpartyId = "counterparty-started",
            Counterparty = new Counterparty(ORGANIZATION_ID, "Example Corp") { Id = "counterparty-started" },
            JobId = "job-started",
            IdempotencyKey = "idempotency-started",
            Variables = new Dictionary<string, object>
            {
                ["ReviewYear"] = "2025",
                ["PeerCount"] = 3,
                ["Ratio"] = 0.25,
                ["IncludeAppendix"] = true,
                ["Peers"] = new List<object> { "peer-1", "peer-2" },
            },
            IsBaseline = true,
            WorkflowState = WorkflowState.Queued,
            FailureKind = "ModelError",
            FailureDetail = "The model refused once.",
            WarningKind = "SlowDocument",
            WarningDetail = "One page took a minute.",
            FreezeSourceExecutionId = "execution-source",
            FreezeSourceCaseId = "case-1",
            FreezeReplayedCount = 2,
            FreezeEligibleCount = 4,
            StartedAt = Created.AddMinutes(1),
            CompletedAt = Created.AddMinutes(5),
            CreatedAt = Created,
            UpdatedAt = Created,
        };

        WorkflowExecutionDto answer = WorkflowExecutionProjections.DtoOf(execution);

        AssertMatchesFixture(answer, "WorkflowExecutionStarted");
    }

    [Fact]
    public async Task AWorkflowSummary_ReadsAsTheSameJson()
    {
        Workflow workflow = BuildWorkflow("workflow-summary", WORKFLOW_TYPE_ID);
        _ = Core.WorkflowTypes.Add(BuildType(WORKFLOW_TYPE_ID, "Corporate Read", categoryId: null, ORGANIZATION_ID, "type-corporation", "type-hedge-fund"));
        _ = Core.Workflows.Add(workflow);
        PinVariableIds(Core, workflow);
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();

        ErrorOr<WorkflowDto> result = await Handler<GetWorkflowSummaryByTypeQueryHandler>().Handle(
            new GetWorkflowSummaryByTypeQuery(ORGANIZATION_ID, WORKFLOW_TYPE_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        AssertMatchesFixture(Sorted(result.Value), "WorkflowSummary");
    }

    [Fact]
    public async Task AWorkflowExecution_ReadsAsTheSameJson()
    {
        _ = Core.WorkflowTypes.Add(new WorkflowType(WORKFLOW_TYPE_ID, "Corporate Read"));
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow workflow = BuildWorkflow("workflow-execution-snapshot");
        var counterparty = new Counterparty(ORGANIZATION_ID, "Example Corp") { Id = "counterparty-read", CreatedAt = Created, UpdatedAt = Created };
        var execution = new WorkflowExecution
        {
            Id = "execution-read",
            WorkflowId = workflow.Id,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            Title = "Annual review",
            UserId = USER_ID,
            OrganizationId = ORGANIZATION_ID,
            ReviewDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            AnalystName = "Ada Lovelace",
            CounterpartyId = counterparty.Id,
            JobId = null,
            Variables = new Dictionary<string, object>
            {
                ["ReviewYear"] = "2025",
                ["Secret"] = "do-not-show",
                ["PeerCount"] = 3,
                ["IncludeAppendix"] = true,
            },
            IsBaseline = false,
            WorkflowState = WorkflowState.Draft,
            FailureKind = "ModelError",
            FailureDetail = "The model refused once.",
            WarningKind = "SlowDocument",
            WarningDetail = "One page took a minute.",
            FreezeSourceExecutionId = "execution-source",
            FreezeSourceCaseId = "case-1",
            FreezeReplayedCount = 2,
            FreezeEligibleCount = 4,
            StartedAt = Created.AddMinutes(1),
            CompletedAt = Created.AddMinutes(5),
            CreatedAt = Created,
            UpdatedAt = Created,
        };
        _ = UserData.Workflows.Add(workflow);
        PinVariableIds(UserData, workflow);
        _ = UserData.Counterparties.Add(counterparty);
        _ = UserData.WorkflowExecutions.Add(execution);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();

        ErrorOr<WorkflowExecutionDto> result = await Handler<GetWorkflowExecutionQueryHandler>().Handle(
            new GetWorkflowExecutionQuery(ORGANIZATION_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description)));
        AssertMatchesFixture(result.Value, "WorkflowExecution");
    }

    [Fact]
    public async Task ADeletedWorkflowSnapshot_LeavesTheVariablesUnlabelled()
    {
        _ = Core.WorkflowTypes.Add(new WorkflowType(WORKFLOW_TYPE_ID, "Corporate Read"));
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Workflow workflow = BuildWorkflow("workflow-deleted-snapshot");
        workflow.IsDeleted = true;
        _ = UserData.Workflows.Add(workflow);
        _ = UserData.WorkflowExecutions.Add(new WorkflowExecution
        {
            Id = "execution-deleted-snapshot",
            WorkflowId = workflow.Id,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            OrganizationId = ORGANIZATION_ID,
            Variables = new Dictionary<string, object> { ["ReviewYear"] = "2025" },
        });
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
        UserData.ChangeTracker.Clear();

        ErrorOr<WorkflowExecutionDto> result = await Handler<GetWorkflowExecutionQueryHandler>().Handle(
            new GetWorkflowExecutionQuery(ORGANIZATION_ID, USER_ID, "execution-deleted-snapshot"),
            TestContext.Current.CancellationToken);

        WorkflowExecutionVariableValueDto year = Assert.Single(result.Value.VariableValues!);
        Assert.Equal("reviewYear", year.DisplayName);
    }

    private static List<WorkflowCategoryDto> Sorted(List<WorkflowCategoryDto> categories)
    {
        foreach (WorkflowCategoryDto category in categories)
        {
            if (category.Workflows is null)
            {
                continue;
            }

            category.Workflows = [.. category.Workflows.OrderBy(workflow => workflow.Id).Select(Sorted)];
        }

        return categories;
    }

    private static WorkflowDto Sorted(WorkflowDto workflow)
    {
        workflow.Variables = [.. workflow.Variables.OrderBy(variable => variable.Id)];
        workflow.DocumentClasses = [.. workflow.DocumentClasses.OrderBy(documentClass => documentClass.Key)];
        workflow.CounterpartyTypeMappings = [.. workflow.CounterpartyTypeMappings.Order()];
        return workflow;
    }

    private async Task SeedCategoriesAsync()
    {
        var corporate = new WorkflowTypeCategory { Id = "category-corporate", Name = "Corporate", Description = "Corporate credit.", DisplayOrder = 2 };
        var funds = new WorkflowTypeCategory { Id = "category-funds", Name = "Funds", DisplayOrder = 1 };
        var hidden = new WorkflowTypeCategory { Id = "category-hidden", Name = "Hidden", DisplayOrder = 3 };
        Core.WorkflowTypeCategories.AddRange(corporate, funds, hidden);
        Core.WorkflowTypes.AddRange(
            BuildType(WORKFLOW_TYPE_ID, "Corporate Read", corporate.Id, ORGANIZATION_ID, "type-corporation", "type-hedge-fund"),
            BuildType("CorpReadEmpty", "Corporate Without Workflows", corporate.Id, ORGANIZATION_ID),
            BuildType("CorpReadOthers", "Corporate For Someone Else", corporate.Id, "org-someone-else"),
            BuildType("FundRead", "Fund Read", funds.Id, ORGANIZATION_ID),
            BuildType("HiddenRead", "Hidden Read", hidden.Id, "org-someone-else"));
        Workflow second = BuildWorkflow("workflow-category-second", WORKFLOW_TYPE_ID);
        Workflow plain = BuildWorkflow("workflow-category-plain", WORKFLOW_TYPE_ID);
        Workflow hiddenWorkflow = BuildWorkflow("workflow-category-hidden", "HiddenRead");
        Workflow othersWorkflow = BuildWorkflow("workflow-category-others", "CorpReadOthers");
        Core.Workflows.AddRange(second, plain, hiddenWorkflow, othersWorkflow);
        PinVariableIds(Core, second);
        PinVariableIds(Core, plain);
        PinVariableIds(Core, hiddenWorkflow);
        PinVariableIds(Core, othersWorkflow);
        _ = await Core.SaveChangesAsync(TestContext.Current.CancellationToken);
        Core.ChangeTracker.Clear();
    }

    private static WorkflowType BuildType(string id, string name, string? categoryId, string organizationId, params string[] counterpartyTypeIds)
    {
        return new WorkflowType(id, name)
        {
            CategoryId = categoryId,
            CounterpartyTypeMappings = [.. counterpartyTypeIds.Select(counterpartyTypeId => new WorkflowTypeCounterpartyTypeMap { WorkflowTypeId = id, CounterpartyTypeId = counterpartyTypeId })],
            OrganizationAccess = [new WorkflowTypeOrganizationAccess { WorkflowTypeId = id, OrganizationId = organizationId }],
        };
    }

    private static void PinVariableIds(DbContext context, Workflow workflow)
    {
        foreach (WorkflowVariable variable in workflow.Variables)
        {
            context.Entry(variable).Property(item => item.Id).CurrentValue = $"{workflow.Id}-variable-{variable.ReplacementTag}";
        }
    }

    private static Workflow BuildWorkflow(string id, string? workflowTypeId = null)
    {
        var workflow = new Workflow
        {
            Id = id,
            WorkflowTypeId = workflowTypeId,
            Name = "Corporate review",
            Title = "Corporate review",
            Description = "Reviews a corporate.",
            OrganizationId = ORGANIZATION_ID,
            CreatedAt = Created,
            UpdatedAt = Created,
        };

        _ = new DocumentClass(workflow, "AnnualReport")
        {
            Id = $"{id}-class-annual",
            DisplayName = "Annual Report",
            Description = "The audited annual report.",
            LlmDescription = "An annual report with audited statements.",
            Requirement = DocumentClassRequirement.Required,
            ShouldBeSingular = true,
            DocumentCategoryId = "category-financials",
            CreatedAt = Created,
            UpdatedAt = Created,
        };
        _ = new DocumentClass(workflow, "Output")
        {
            Id = $"{id}-class-output",
            DisplayName = "Output",
            Description = "The generated workbook.",
            Requirement = DocumentClassRequirement.Optional,
            IsOutputDocument = true,
            CreatedAt = Created,
            UpdatedAt = Created,
        };
        _ = new WorkflowVariable(workflow, "ReviewYear")
        {
            DisplayName = "Review year",
            Description = "The year under review.",
            Type = WorkflowVariableTypes.Dropdown,
            IsRequired = true,
            DefaultValue = "2025",
            Options = new Dictionary<string, string> { ["2024"] = "2024", ["2025"] = "2025" },
        };
        _ = new WorkflowVariable(workflow, "Secret")
        {
            DisplayName = "Secret",
            Description = "A confidential input.",
            IsConfidential = true,
            IsHidden = true,
        };
        return workflow;
    }
}
