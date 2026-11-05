using ErrorOr;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.Workflows.Versions;

public class GetWorkflowHistoryQueryTests : IDisposable
{
    private const string ORG = "org-test";
    private const string TYPE = "WT";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _coreContext;
    private readonly UserDataDbContext _userDataContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public GetWorkflowHistoryQueryTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _coreContext = _serviceProvider.GetRequiredService<CoreDbContext>();
        _userDataContext = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _coreUnitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        _userDataUnitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();

        _coreContext.GrantWorkflowTypeAccess(TYPE, ORG);
        _ = _coreContext.SaveChanges();
    }

    public void Dispose()
    {
        _coreContext.Dispose();
        _userDataContext.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task History_ResolvesAuthorDisplayName()
    {
        _ = _userDataContext.Users.Add(new User(ORG) { Id = "author-1", FirstName = "Grace", LastName = "Hopper" });
        await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        SeedVersion("author-1");
        await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<WorkflowHistoryPageDto> result = await Handler().Handle(
            new GetWorkflowHistoryQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        WorkflowVersionSummaryDto summary = Assert.Single(result.Value.Versions);
        Assert.Equal("Grace Hopper", summary.CreatedByName);
        Assert.Equal("author-1", summary.CreatedByUserId);
        Assert.Equal(new string('1', 64), summary.ContentHash);
    }

    [Fact]
    public async Task History_FallsBackToUserId_WhenAuthorUnknown()
    {
        SeedVersion("ghost");
        await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<WorkflowHistoryPageDto> result = await Handler().Handle(
            new GetWorkflowHistoryQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        WorkflowVersionSummaryDto summary = Assert.Single(result.Value.Versions);
        Assert.Equal("ghost", summary.CreatedByName);
    }

    private GetWorkflowHistoryQueryHandler Handler()
    {
        return new GetWorkflowHistoryQueryHandler(_coreUnitOfWork, new UserDisplayNameResolver(_userDataUnitOfWork));
    }

    private void SeedVersion(string createdByUserId)
    {
        _ = _coreContext.WorkflowVersions.Add(new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = new string('1', 64),
            WorkflowJson = "{}",
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
        });
    }
}
