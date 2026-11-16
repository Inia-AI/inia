using IniaAi.Application.Commands.Documents.UpdateDocument;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.Counterparties.ExportCounterparties;
using IniaAi.Application.Queries.Documents.GetDocument;
using IniaAi.Application.Queries.Documents.GetDocumentPage;
using IniaAi.Application.Queries.ModelPricing.GetModelPricing;
using IniaAi.Application.Queries.Organizations.GetUserTenants;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Common.Enums;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Responses.Documents;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;
using IniaAi.WebApi.Contracts.Responses.Organizations;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Models.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

// The read paths that once loaded an entity graph and mapped it in memory now project to their
// DTO in the query. Their projections read value-converted JSON columns (execution variables,
// variable options, block properties, document sources on SQL Server), primitive collections
// (embedding vectors, document sources on PostgreSQL) and build nested lists, which the in-memory
// provider that runs their tests in CI does not translate. This runs each read on both providers.
public abstract class ReadPathsOnBothProvidersTests
{
    private const string TEST_DB_PREFIX = "iniaai_readpaths_";
    private const string GUARD_NAME = "the read path guard";
    private const string ORGANIZATION_ID = "org-read-paths";
    private const string USER_ID = "user-read-paths";
    private const string WORKFLOW_TYPE_ID = "ReadPathsType";
    private const string WORKFLOW_CATEGORY_ID = "workflow-category-read-paths";
    private const string OTHER_ORGANIZATION_ID = "org-read-paths-other";
    private const string DOCUMENT_ID = "document-read-paths";

    private static readonly DateTime _created = new(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc);

    private readonly ICoreDatabaseFixture _core;
    private readonly string _provider;
    private readonly string _skipReason;

    protected ReadPathsOnBothProvidersTests(ICoreDatabaseFixture core, string provider, string skipReason)
    {
        _core = core;
        _provider = provider;
        _skipReason = skipReason;
    }

    [Fact]
    public async Task Every_read_path_projects_on_a_real_database()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        if (!_core.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }

        DbContextOptions<UserDataDbContext> userDataOptions = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(
            _provider, TEST_DB_PREFIX, GUARD_NAME, cancellationToken);

        try
        {
            await using (UserDataDbContext setup = new(userDataOptions))
            {
                await setup.Database.MigrateAsync(cancellationToken);
                await SeedUserDataAsync(setup, cancellationToken);
            }

            await using (CoreDbContext core = _core.CreateContext())
            {
                await SeedCoreAsync(core, cancellationToken);
            }

            await using ServiceProvider services = BothProvidersHandlers.BuildServices(userDataOptions, _core);
            await AssertEveryReadPathAsync(services, cancellationToken);
        }
        finally
        {
            await using UserDataDbContext cleanup = new(userDataOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    private static async Task AssertEveryReadPathAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        ModelPricingDto pricing = await BothProvidersHandlers.HandleAsync<GetModelPricingQueryHandler, ModelPricingDto>(
            services, handler => handler.Handle(new GetModelPricingQuery("pricing-read-paths"), cancellationToken));
        Assert.Equal(["Input", "Output"], pricing.Components.Select(component => component.ComponentType.ToString()).Order());

        CounterpartyBundleDto bundle = await BothProvidersHandlers.HandleAsync<ExportCounterpartiesQueryHandler, CounterpartyBundleDto>(
            services, handler => handler.Handle(new ExportCounterpartiesQuery(ORGANIZATION_ID, USER_ID, "guard"), cancellationToken));
        CounterpartyBundleEntryDto child = Assert.Single(bundle.Counterparties, entry => entry.Id == "counterparty-child");
        Assert.Equal("counterparty-parent", child.ParentId);
        Assert.Equal([CounterpartyType.TYPE_CORPORATION], child.CounterpartyTypeNames);
        Assert.Equal("EMEA", Assert.Single(bundle.Counterparties, entry => entry.Id == "counterparty-parent").Properties!["Region"]);

        List<UserTenantDto> tenants = await BothProvidersHandlers.HandleAsync<GetUserTenantsQueryHandler, List<UserTenantDto>>(
            services, handler => handler.Handle(new GetUserTenantsQuery(USER_ID), cancellationToken));
        Assert.Equal(["Read Paths Org"], tenants.Select(tenant => tenant.Name));

        WorkflowDto summary = await BothProvidersHandlers.HandleAsync<GetWorkflowSummaryByTypeQueryHandler, WorkflowDto>(
            services, handler => handler.Handle(new GetWorkflowSummaryByTypeQuery(ORGANIZATION_ID, WORKFLOW_TYPE_ID), cancellationToken));
        Assert.Equal("Read Paths Type", summary.WorkflowTypeName);
        Assert.Equal(["type-corporation"], summary.CounterpartyTypeMappings);
        Assert.Equal(["AnnualReport", "Statement"], summary.DocumentClassKeys);
        Assert.Equal("2025", Assert.Single(summary.Variables, variable => variable.ReplacementTag == "ReviewYear").Options["2025"]);

        List<WorkflowCategoryDto> categories = await BothProvidersHandlers.HandleAsync<ListWorkflowCategoriesQueryHandler, List<WorkflowCategoryDto>>(
            services, handler => handler.Handle(new ListWorkflowCategoriesQuery(ORGANIZATION_ID, USER_ID, IncludeWorkflows: true), cancellationToken));
        WorkflowDto listed = Assert.Single(Assert.Single(categories, category => category.Id == WORKFLOW_CATEGORY_ID).Workflows!);
        Assert.Equal("Read Paths Type", listed.WorkflowTypeName);
        Assert.Equal(["type-corporation"], listed.CounterpartyTypeMappings);
        Assert.Equal(["AnnualReport", "Statement"], listed.DocumentClassKeys);
        Assert.Equal("2025", Assert.Single(listed.Variables, variable => variable.ReplacementTag == "ReviewYear").Options["2025"]);

        List<WorkflowCategorySummaryDto> menu = await BothProvidersHandlers.HandleAsync<ListWorkflowCategorySummariesQueryHandler, List<WorkflowCategorySummaryDto>>(
            services, handler => handler.Handle(new ListWorkflowCategorySummariesQuery(ORGANIZATION_ID, USER_ID), cancellationToken));
        WorkflowSummaryDto entry = Assert.Single(Assert.Single(menu, category => category.Id == WORKFLOW_CATEGORY_ID).Workflows!);
        Assert.Equal("Read Paths Type", entry.WorkflowTypeName);

        WorkflowExecutionDto execution = await BothProvidersHandlers.HandleAsync<GetWorkflowExecutionQueryHandler, WorkflowExecutionDto>(
            services, handler => handler.Handle(new GetWorkflowExecutionQuery(ORGANIZATION_ID, USER_ID, "execution-read-paths"), cancellationToken));
        Assert.Equal("Example Corp", execution.CounterpartyName);
        Assert.Equal(3, Assert.Single(execution.Variables!, variable => string.Equals(variable.Key, "PeerCount", StringComparison.OrdinalIgnoreCase)).Value.GetInt32());
        Assert.Equal(TimeSpan.FromMinutes(4), execution.Duration);
        Assert.Equal("2025", Assert.Single(execution.VariableValues!, value => value.ReplacementTag == "ReviewYear").DisplayValue);

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            ICoreUnitOfWork core = scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();
            MadeByReader reader = Assert.Single(await MadeByLinks.ReadersOfAsync(core, WORKFLOW_TYPE_ID, ["Spread", "Another output"], ORGANIZATION_ID, cancellationToken));
            Assert.Equal("Read Paths Type", reader.WorkflowTypeName);
            Assert.Equal(["AnnualReport", "Statement"], reader.Inputs.Select(input => input.Key).Order());
            Assert.Empty(await MadeByLinks.ReadersOfAsync(core, WORKFLOW_TYPE_ID, ["Spread"], "another-organization", cancellationToken));
            Assert.Null(await MadeByLinks.OutputReachableByAsync(core, ORGANIZATION_ID, WORKFLOW_TYPE_ID, "Spread", cancellationToken));
        }

        DocumentDetailDto document = await BothProvidersHandlers.HandleAsync<GetDocumentQueryHandler, DocumentDetailDto>(
            services, handler => handler.Handle(new GetDocumentQuery(ORGANIZATION_ID, USER_ID, DOCUMENT_ID), cancellationToken));
        Assert.Equal(2, document.PageCount);
        Assert.Equal(482_113, document.SizeBytes);
        Assert.Equal(".pdf", document.Extension);
        Assert.Equal(["User Upload", "Email"], document.Sources);

        PageDetailDto embedded = await BothProvidersHandlers.HandleAsync<GetDocumentPageQueryHandler, PageDetailDto>(
            services, handler => handler.Handle(new GetDocumentPageQuery(ORGANIZATION_ID, USER_ID, DOCUMENT_ID, 1), cancellationToken));
        Assert.Equal(4, embedded.EmbeddingDimensions);
        Assert.Equal("titan-embed-v2", embedded.EmbeddingModel);
        Assert.Equal(embedded.Text, embedded.ReadableText);
        Assert.Equal(200, embedded.TextPreview!.Length);
        Assert.Equal("Overview, Revenue", embedded.SectionTitles);

        PageDetailDto needsReview = await BothProvidersHandlers.HandleAsync<GetDocumentPageQueryHandler, PageDetailDto>(
            services, handler => handler.Handle(new GetDocumentPageQuery(ORGANIZATION_ID, USER_ID, DOCUMENT_ID, 2), cancellationToken));
        Assert.True(needsReview.OcrNeedsReview);
        Assert.Equal("A chart of leverage by quarter.", needsReview.ReadableText);
        Assert.Null(needsReview.EmbeddingDimensions);
        Assert.Null(needsReview.SectionTitles);

        DocumentDetailDto renamed = await BothProvidersHandlers.HandleAsync<UpdateDocumentCommandHandler, DocumentDetailDto>(
            services, handler => handler.Handle(
                new UpdateDocumentCommand(ORGANIZATION_ID, USER_ID, DOCUMENT_ID, "Renamed report", null, null, null, null, null, null),
                cancellationToken));
        Assert.Equal("Renamed report", renamed.Name);
        Assert.Equal(2, renamed.PageCount);
    }

    private static async Task SeedUserDataAsync(UserDataDbContext context, CancellationToken cancellationToken)
    {
        _ = context.Organizations.Add(new Organization("Read Paths Org", ORGANIZATION_ID));
        _ = context.Organizations.Add(new Organization("Closed Org", OTHER_ORGANIZATION_ID) { IsActive = false });
        _ = context.Users.Add(new User(ORGANIZATION_ID) { Id = USER_ID, UserName = "reader@example.com", Email = "reader@example.com" });
        context.UserOrganizations.AddRange(
            new UserOrganization { UserId = USER_ID, OrganizationId = ORGANIZATION_ID, IsDefault = true },
            new UserOrganization { UserId = USER_ID, OrganizationId = OTHER_ORGANIZATION_ID });
        var corporation = new CounterpartyType(CounterpartyType.TYPE_CORPORATION) { Id = "type-corporation" };
        Counterparty parent = new(ORGANIZATION_ID, "Example Corp") { Id = "counterparty-parent", CounterpartyTypes = [corporation] };
        parent.Properties = new Dictionary<string, string> { ["Region"] = "EMEA" };
        context.Counterparties.AddRange(
            parent,
            new Counterparty(ORGANIZATION_ID, "Example Child") { Id = "counterparty-child", ParentId = parent.Id, CounterpartyTypes = [corporation] });
        _ = await context.SaveChangesAsync(cancellationToken);

        var document = new Document(new FileMetadata(ORGANIZATION_ID, "Annual report.pdf", 482_113, StorageProvider.Database), MediaType.ApplicationPdf)
        {
            Id = DOCUMENT_ID,
            OrganizationId = ORGANIZATION_ID,
            Name = "Annual report",
            Sources = ["User Upload", "Email"],
        };
        _ = new Page(document, 1, 1)
        {
            Status = PageStatus.ProcessedSuccessfully,
            Text = new string('A', 300),
            SectionTitles = "Overview, Revenue",
            EmbeddingVector = new EmbeddingVector("titan-embed-v2", [0.1, 0.2, 0.3, 0.4]),
        };
        _ = new Page(document, 2, 2)
        {
            Status = PageStatus.OCRNeedsReview,
            Overview = "A chart of leverage by quarter.",
            SectionTitles = "   ",
        };
        _ = context.Documents.Add(document);
        _ = await context.SaveChangesAsync(cancellationToken);

        Workflow snapshot = BuildWorkflow("workflow-read-paths-snapshot", screening: false);
        snapshot.OrganizationId = ORGANIZATION_ID;
        _ = context.Workflows.Add(snapshot);
        _ = context.WorkflowExecutions.Add(new WorkflowExecution
        {
            Id = "execution-read-paths",
            WorkflowId = snapshot.Id,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            Title = "Annual review",
            UserId = USER_ID,
            OrganizationId = ORGANIZATION_ID,
            CounterpartyId = parent.Id,
            Variables = new Dictionary<string, object> { ["ReviewYear"] = "2025", ["PeerCount"] = 3 },
            WorkflowState = WorkflowState.Draft,
            StartedAt = _created,
            CompletedAt = _created.AddMinutes(4),
        });
        _ = await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCoreAsync(CoreDbContext context, CancellationToken cancellationToken)
    {
        if (await context.WorkflowTypes.AnyAsync(type => type.Id == WORKFLOW_TYPE_ID, cancellationToken))
        {
            return;
        }

        _ = context.WorkflowTypeCategories.Add(new WorkflowTypeCategory { Id = WORKFLOW_CATEGORY_ID, Name = "Read Paths Workflows" });
        _ = context.WorkflowTypes.Add(new WorkflowType(WORKFLOW_TYPE_ID, "Read Paths Type")
        {
            CategoryId = WORKFLOW_CATEGORY_ID,
            CounterpartyTypeMappings = [new WorkflowTypeCounterpartyTypeMap { WorkflowTypeId = WORKFLOW_TYPE_ID, CounterpartyTypeId = "type-corporation" }],
            OrganizationAccess = [new WorkflowTypeOrganizationAccess { WorkflowTypeId = WORKFLOW_TYPE_ID, OrganizationId = ORGANIZATION_ID }],
        });
        Workflow workflow = BuildWorkflow("workflow-read-paths", screening: true);
        workflow.WorkflowTypeId = WORKFLOW_TYPE_ID;
        _ = context.Workflows.Add(workflow);

        _ = context.ModelPricings.Add(new ModelPricing
        {
            Id = "pricing-read-paths",
            Provider = "aws-bedrock",
            ModelId = "claude-read-paths",
            DisplayName = "Claude Read Paths",
            PricingType = PricingType.Llm,
            Components =
            [
                new ModelPricingComponent { Id = "pricing-read-paths-input", ModelPricingId = "pricing-read-paths", ComponentType = PricingComponentType.Input, Cost = 3m },
                new ModelPricingComponent { Id = "pricing-read-paths-output", ModelPricingId = "pricing-read-paths", ComponentType = PricingComponentType.Output, Cost = 15m },
            ],
        });

        _ = await context.SaveChangesAsync(cancellationToken);
    }

    private static Workflow BuildWorkflow(string id, bool screening)
    {
        var workflow = new Workflow
        {
            Id = id,
            Name = "Read paths",
            Title = "Read paths",
        };
        _ = new DocumentClass(workflow, "AnnualReport") { DisplayName = "Annual Report", Description = "An audited report.", Requirement = DocumentClassRequirement.Required };
        _ = new DocumentClass(workflow, "Statement")
        {
            DisplayName = "Statement",
            Description = string.Empty,
            Requirement = DocumentClassRequirement.Required,
            MadeByWorkflowTypeId = WORKFLOW_TYPE_ID,
            MadeByDocumentClassKey = "Spread",
        };
        _ = new WorkflowVariable(workflow, "ReviewYear")
        {
            DisplayName = "Review year",
            Type = WorkflowVariableTypes.Dropdown,
            Options = new Dictionary<string, string> { ["2024"] = "2024", ["2025"] = "2025" },
        };
        return workflow;
    }

}

public sealed class ReadPathsOnBothProvidersPostgresTests(PostgresCoreDatabaseFixture fixture)
    : ReadPathsOnBothProvidersTests(fixture, BothProvidersUserDataDatabase.POSTGRESQL, "Set INIA_TEST_PG to run the read path guard on PostgreSQL."),
    IClassFixture<PostgresCoreDatabaseFixture>;

public sealed class ReadPathsOnBothProvidersSqlServerTests(SqlServerCoreDatabaseFixture fixture)
    : ReadPathsOnBothProvidersTests(fixture, BothProvidersUserDataDatabase.MSSQL, "Set INIA_TEST_MSSQL to run the read path guard on SQL Server."),
    IClassFixture<SqlServerCoreDatabaseFixture>;
