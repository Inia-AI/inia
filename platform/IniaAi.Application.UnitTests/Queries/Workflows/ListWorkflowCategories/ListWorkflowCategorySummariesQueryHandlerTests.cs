using ErrorOr;
using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.Workflows.ListWorkflowCategories;

public class ListWorkflowCategorySummariesQueryHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string OTHER_ORG = "org-other";
    private const string USER = "user-test";
    private const string OTHER_USER = "user-other";
    private const string CATEGORY = "cat-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _coreContext;
    private readonly UserDataDbContext _userDataContext;
    private readonly ListWorkflowCategorySummariesQueryHandler _handler;

    public ListWorkflowCategorySummariesQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();

        _coreContext = _serviceProvider.GetRequiredService<CoreDbContext>();
        _userDataContext = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _handler = new ListWorkflowCategorySummariesQueryHandler(
            _serviceProvider.GetRequiredService<ICoreUnitOfWork>(),
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            new NoCreditCost());

        _ = _coreContext.Set<WorkflowTypeCategory>().Add(new WorkflowTypeCategory
        {
            Id = CATEGORY,
            Name = "Monitoring",
            DisplayOrder = 1,
        });

        _ = _userDataContext.Users.Add(new User(ORG) { Id = USER });
        _ = _userDataContext.Users.Add(new User(ORG) { Id = OTHER_USER });
    }

    [Fact]
    public async Task NoVisibilityRows_ReturnsEveryTypeTheOrganizationCanAccess()
    {
        await SeedTypeAsync("WT-A", ORG);
        await SeedTypeAsync("WT-B", ORG);

        List<string> visible = await ListVisibleTypeIdsAsync();

        Assert.Equal(["WT-A", "WT-B"], visible);
    }

    [Fact]
    public async Task VisibilityRows_ReturnOnlyTheChosenTypes()
    {
        await SeedTypeAsync("WT-A", ORG);
        await SeedTypeAsync("WT-B", ORG);
        await SeedVisibilityAsync(USER, "WT-A");

        List<string> visible = await ListVisibleTypeIdsAsync();

        Assert.Equal(["WT-A"], visible);
    }

    [Fact]
    public async Task AnotherUsersVisibilityRows_DoNotNarrowThisUser()
    {
        await SeedTypeAsync("WT-A", ORG);
        await SeedTypeAsync("WT-B", ORG);
        await SeedVisibilityAsync(OTHER_USER, "WT-A");

        List<string> visible = await ListVisibleTypeIdsAsync();

        Assert.Equal(["WT-A", "WT-B"], visible);
    }

    [Fact]
    public async Task ChosenTypeWithoutOrganizationAccess_StaysHidden()
    {
        await SeedTypeAsync("WT-A", ORG);
        await SeedTypeAsync("WT-Foreign", OTHER_ORG);
        await SeedVisibilityAsync(USER, "WT-A");
        await SeedVisibilityAsync(USER, "WT-Foreign");

        List<string> visible = await ListVisibleTypeIdsAsync();

        Assert.Equal(["WT-A"], visible);
    }

    private async Task<List<string>> ListVisibleTypeIdsAsync()
    {
        ErrorOr<List<WorkflowCategorySummaryDto>> result = await _handler.Handle(
            new ListWorkflowCategorySummariesQuery(ORG, USER),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        return [.. result.Value
            .SelectMany(c => c.Workflows ?? [])
            .Select(w => w.WorkflowTypeId)
            .Order()];
    }

    private async Task SeedTypeAsync(string typeId, string organizationId)
    {
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType(typeId, typeId) { CategoryId = CATEGORY });
        _coreContext.GrantWorkflowTypeAccess(typeId, organizationId);
        _ = _coreContext.Workflows.Add(new Workflow(typeId) { WorkflowTypeId = typeId });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedVisibilityAsync(string userId, string typeId)
    {
        _ = _userDataContext.UserWorkflowTypeVisibilities.Add(new UserWorkflowTypeVisibility
        {
            UserId = userId,
            WorkflowTypeId = typeId,
        });
        _ = await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _coreContext.Dispose();
        _userDataContext.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
