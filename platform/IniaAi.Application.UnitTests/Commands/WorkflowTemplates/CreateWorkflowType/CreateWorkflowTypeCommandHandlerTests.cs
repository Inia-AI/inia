using ErrorOr;
using IniaAi.Application;
using IniaAi.Application.Commands.WorkflowTemplates.CreateWorkflowType;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Commands.WorkflowTemplates.CreateWorkflowType;

/// <summary>
/// The admin app's Add Workflow Type over a real database: the new type reaches the creator's own
/// organization, and an id that is already taken comes back as an error instead of a failed save.
/// </summary>
public sealed class CreateWorkflowTypeCommandHandlerTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-creator";
    private const string OTHER_ORGANIZATION_ID = "org-other";
    private const string USER_ID = "user-creator";
    private const string TYPE_ID = "qa-se01";
    private const string CATEGORY_ID = "test-workflows";
    private const string COUNTERPARTY_TYPE_ID = "ct-bank";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly CoreDbContext _core;
    private readonly CreateWorkflowTypeCommandHandler _handler;

    public CreateWorkflowTypeCommandHandlerTests()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddTestDatabaseServices();
        _ = services.AddSeamDefaults();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _core = _scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _handler = ActivatorUtilities.CreateInstance<CreateWorkflowTypeCommandHandler>(
            _scope.ServiceProvider,
            new WorkflowTemplateSavePreflight(_scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>()));

        _ = _core.WorkflowTypeCategories.Add(new WorkflowTypeCategory { Id = CATEGORY_ID, Name = "Test Workflows" });
        _ = _core.SaveChanges();
        _core.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public async Task CreatesTheType_WithItsCategoryAndCounterpartyTypes()
    {
        ErrorOr<Created> result = await _handler.Handle(Command(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        WorkflowType created = await _core.WorkflowTypes
            .Include(type => type.CounterpartyTypeMappings)
            .SingleAsync(type => type.Id == TYPE_ID, TestContext.Current.CancellationToken);
        Assert.Equal("QA SE01", created.Name);
        Assert.Equal("Seam check", created.Description);
        Assert.Equal(CATEGORY_ID, created.CategoryId);
        Assert.Equal([COUNTERPARTY_TYPE_ID], created.CounterpartyTypeMappings!.Select(map => map.CounterpartyTypeId));
    }

    [Fact]
    public async Task GrantsTheCreatorsOrganization_AndNoOther()
    {
        _ = await _handler.Handle(Command(), TestContext.Current.CancellationToken);

        List<string> granted = await _core.WorkflowTypeOrganizationAccess
            .Where(access => access.WorkflowTypeId == TYPE_ID)
            .Select(access => access.OrganizationId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([ORGANIZATION_ID], granted);
    }

    [Fact]
    public async Task TheNewType_ReachesTheCreatorsAnalysts_UnderItsCategory_Only()
    {
        _ = await _handler.Handle(Command(), TestContext.Current.CancellationToken);
        _ = _core.Workflows.Add(new Workflow("QA SE01") { WorkflowTypeId = TYPE_ID, OrganizationId = ORGANIZATION_ID, UserId = USER_ID });
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<WorkflowCategorySummaryDto> creators = await SummariesForAsync(ORGANIZATION_ID);
        List<WorkflowCategorySummaryDto> others = await SummariesForAsync(OTHER_ORGANIZATION_ID);

        WorkflowCategorySummaryDto category = Assert.Single(creators);
        Assert.Equal(CATEGORY_ID, category.Id);
        Assert.Equal(TYPE_ID, Assert.Single(category.Workflows!).WorkflowTypeId);
        Assert.Empty(others);
    }

    [Fact]
    public async Task ATakenId_IsRefused_AndTheExistingTypeKeepsItsAccess()
    {
        _ = _core.WorkflowTypes.Add(new WorkflowType(TYPE_ID, "Existing"));
        _core.GrantWorkflowTypeAccess(TYPE_ID, OTHER_ORGANIZATION_ID);
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);
        _core.ChangeTracker.Clear();

        ErrorOr<Created> result = await _handler.Handle(Command(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(WorkflowTypeErrorFactory.ALREADY_EXISTS_CODE, result.FirstError.Code);
        Assert.Contains(TYPE_ID, result.FirstError.Description);
        Assert.Equal("Existing", (await _core.WorkflowTypes.SingleAsync(TestContext.Current.CancellationToken)).Name);
        Assert.Equal(
            [OTHER_ORGANIZATION_ID],
            await _core.WorkflowTypeOrganizationAccess.Select(access => access.OrganizationId).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheIdOfADeletedType_IsRefused_UntilTheCleanupRemovesIt()
    {
        _ = _core.WorkflowTypes.Add(new WorkflowType(TYPE_ID, "Deleted") { RetiredAt = DateTime.UtcNow });
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);
        _core.ChangeTracker.Clear();

        ErrorOr<Created> result = await _handler.Handle(Command(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Contains("deleted", result.FirstError.Description);
        Assert.Empty(await _core.WorkflowTypeOrganizationAccess.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefused_AndNothingIsCreated()
    {
        ErrorOr<Created> result = await _handler.Handle(
            Command() with { CategoryId = "no-such-category" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(await _core.WorkflowTypes.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoCategory_CreatesATypeWithoutOne()
    {
        ErrorOr<Created> result = await _handler.Handle(
            Command() with { CategoryId = null },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null((await _core.WorkflowTypes.SingleAsync(TestContext.Current.CancellationToken)).CategoryId);
    }

    private static CreateWorkflowTypeCommand Command()
    {
        return new CreateWorkflowTypeCommand(ORGANIZATION_ID, USER_ID, TYPE_ID, "QA SE01", "Seam check", CATEGORY_ID, [COUNTERPARTY_TYPE_ID]);
    }

    private async Task<List<WorkflowCategorySummaryDto>> SummariesForAsync(string organizationId)
    {
        ListWorkflowCategorySummariesQueryHandler summaries =
            ActivatorUtilities.CreateInstance<ListWorkflowCategorySummariesQueryHandler>(_scope.ServiceProvider);
        ErrorOr<List<WorkflowCategorySummaryDto>> result = await summaries.Handle(
            new ListWorkflowCategorySummariesQuery(organizationId, USER_ID),
            TestContext.Current.CancellationToken);

        return result.Value;
    }
}
