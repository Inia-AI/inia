using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Specifications;

public class WorkflowExecutionsForListingSpecTests : IDisposable
{
    private const string ORG = "org-test";
    private const string OTHER_ORG = "org-other";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly IUserDataUnitOfWork _unitOfWork;

    public WorkflowExecutionsForListingSpecTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();

        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [Fact]
    public async Task NoChosenTypes_ReturnsEveryExecutionInTheOrganization()
    {
        await SeedExecutionAsync(ORG, "HedgeFundsMonitoring");
        await SeedExecutionAsync(ORG, "BankAccountAnalysis");

        List<string> types = await ListTypeIdsAsync([]);

        Assert.Equal(["BankAccountAnalysis", "HedgeFundsMonitoring"], types);
    }

    [Fact]
    public async Task ChosenTypes_ReturnOnlyExecutionsOfThoseTypes()
    {
        await SeedExecutionAsync(ORG, "HedgeFundsMonitoring");
        await SeedExecutionAsync(ORG, "BankAccountAnalysis");

        List<string> types = await ListTypeIdsAsync(["BankAccountAnalysis"]);

        Assert.Equal(["BankAccountAnalysis"], types);
    }

    [Fact]
    public async Task ChosenTypes_NeverReachAnotherOrganizationsExecutions()
    {
        await SeedExecutionAsync(ORG, "BankAccountAnalysis");
        await SeedExecutionAsync(OTHER_ORG, "BankAccountAnalysis");

        List<string> types = await ListTypeIdsAsync(["BankAccountAnalysis"]);

        _ = Assert.Single(types);
    }

    [Fact]
    public async Task GenericExecutions_StayExcludedWhenTypesAreChosen()
    {
        await SeedExecutionAsync(ORG, WorkflowType.GENERIC);
        await SeedExecutionAsync(ORG, "BankAccountAnalysis");

        List<string> types = await ListTypeIdsAsync([WorkflowType.GENERIC, "BankAccountAnalysis"]);

        Assert.Equal(["BankAccountAnalysis"], types);
    }

    private async Task<List<string>> ListTypeIdsAsync(IReadOnlyCollection<string> chosenTypeIds)
    {
        var spec = new WorkflowExecutionsForListingSpec(ORG, chosenTypeIds);

        IList<WorkflowExecution> executions = await _unitOfWork.WorkflowExecutions
            .GetAllAsync(spec, TestContext.Current.CancellationToken);

        return [.. executions.Select(e => e.WorkflowTypeId!).Order()];
    }

    private async Task SeedExecutionAsync(string organizationId, string workflowTypeId)
    {
        _ = _context.WorkflowExecutions.Add(new WorkflowExecution
        {
            OrganizationId = organizationId,
            WorkflowTypeId = workflowTypeId,
            Title = workflowTypeId,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
