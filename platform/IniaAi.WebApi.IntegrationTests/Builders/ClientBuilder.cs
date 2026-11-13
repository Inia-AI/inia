using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.Contracts.Responses.Meta;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace IniaAi.WebApi.IntegrationTests.Builders;

public class ClientBuilder
{
    private const int ORGANIZATION_CLEANUP_ATTEMPTS = 3;

    private readonly WebApplicationFactory<Program> _factory;
    public HttpClient Client { get; }

    public IServiceScope CurrentScope { get; }

    public ClientBuilder()
        : this(new CustomWebApplicationFactory())
    {
    }

    /// <summary>
    /// A host whose services <paramref name="configureTestServices"/> changes after the app's own
    /// registrations, such as a seam a closed module would fill.
    /// </summary>
    public ClientBuilder(Action<IServiceCollection> configureTestServices)
        : this(new CustomWebApplicationFactory().WithWebHostBuilder(host => host.ConfigureTestServices(configureTestServices)))
    {
    }

    /// <summary>
    /// A host started with <paramref name="settings"/>, for a setting the app reads while it
    /// registers its services, where a change to the options afterwards comes too late.
    /// </summary>
    public static ClientBuilder WithSettings(IReadOnlyDictionary<string, string> settings)
    {
        return new ClientBuilder(new CustomWebApplicationFactory().WithWebHostBuilder(host =>
        {
            foreach ((string key, string value) in settings)
            {
                _ = host.UseSetting(key, value);
            }
        }));
    }

    private ClientBuilder(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        Client = _factory.CreateClient();
        CurrentScope = _factory.Services.CreateScope();
    }

    #region Endpoint Responses

    public List<WorkflowExecution>? GetWorkflowOutputsResponse { get; set; }

    public GetMetaVersionResponse? GetMetaVersionResponse { get; set; }

    #endregion Endpoint Responses

    public (IServiceScope Scope, T Service) GetNewScope<T>() where T : notnull
    {
        IServiceScope scope = _factory.Services.CreateScope();
        T service = scope.ServiceProvider.GetRequiredService<T>();
        return (scope, service);
    }

    public async Task CleanCounterpartiesByIdsAsync(IEnumerable<string> ids)
    {
        List<string> idList = [.. ids];
        if (idList.Count == 0)
        {
            return;
        }

        using IServiceScope scope = _factory.Services.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        // Executions and reports left behind by tests that failed mid-run still reference
        // these counterparties; detach them first or the FK blocks the delete and the
        // whole class is reported as a cleanup failure. IgnoreQueryFilters: soft-deleted
        // rows are hidden by the global IsDeleted filter but still hold the FKs.
        _ = await db.WorkflowExecutions
            .IgnoreQueryFilters()
            .Where(e => e.CounterpartyId != null && idList.Contains(e.CounterpartyId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CounterpartyId, (string?)null));
        _ = await db.WorkflowReports
            .IgnoreQueryFilters()
            .Where(r => r.CounterpartyId != null && idList.Contains(r.CounterpartyId))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CounterpartyId, (string?)null));
        _ = await db.WorkflowExecutionCounterparties
            .Where(c => idList.Contains(c.CounterpartyId))
            .ExecuteDeleteAsync();
        _ = await db.Counterparties
            .IgnoreQueryFilters()
            .Where(c => idList.Contains(c.Id))
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// Hard-removes test-seeded executions and the executed-workflow snapshots that share
    /// their ids, without the load-then-remove round-trips the per-class cleanup loops
    /// used to do.
    /// </summary>
    public async Task CleanExecutionsByIdsAsync(IEnumerable<string> ids)
    {
        List<string> idList = [.. ids];
        if (idList.Count == 0)
        {
            return;
        }

        using IServiceScope scope = _factory.Services.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        _ = await db.WorkflowExecutions
            .IgnoreQueryFilters()
            .Where(e => idList.Contains(e.Id))
            .ExecuteDeleteAsync();
        _ = await db.Workflows
            .IgnoreQueryFilters()
            .Where(w => idList.Contains(w.Id))
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// Hard-removes test-seeded organizations together with the dependents that make a
    /// bare org delete impossible: SystemUserSeeder attaches a system user (plus its
    /// UserOrganizations link) to every organization on each in-proc app boot, and token
    /// usage rows may reference the org after a workflow ran under it. Callers must have
    /// removed their own seeded entities (documents, workflows, counterparties) first.
    /// An app that another test class boots meanwhile can link its system user between
    /// these deletes, so a pass that fails runs again and removes that link too.
    /// </summary>
    public async Task CleanOrganizationsByIdsAsync(IEnumerable<string> ids)
    {
        List<string> idList = [.. ids];
        if (idList.Count == 0)
        {
            return;
        }

        using IServiceScope scope = _factory.Services.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        int attempt = 1;
        while (true)
        {
            try
            {
                await DeleteOrganizationsAsync(db, idList);

                return;
            }
            catch (DbException) when (attempt < ORGANIZATION_CLEANUP_ATTEMPTS)
            {
                attempt++;
            }
        }
    }

    private static async Task DeleteOrganizationsAsync(UserDataDbContext db, List<string> idList)
    {
        _ = await db.UserOrganizations
            .Where(uo => idList.Contains(uo.OrganizationId))
            .ExecuteDeleteAsync();
        _ = await db.Users
            .Where(u => u.IsSystem && idList.Contains(u.OrganizationId))
            .ExecuteDeleteAsync();
        _ = await db.Set<TokenUsage>()
            .IgnoreQueryFilters()
            .Where(t => idList.Contains(t.OrganizationId))
            .ExecuteDeleteAsync();
        _ = await db.Organizations
            .IgnoreQueryFilters()
            .Where(o => idList.Contains(o.Id))
            .ExecuteDeleteAsync();
    }

    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        return await sender.Send(request);
    }

    public async Task<TEntity?> FindAsync<TEntity>(params object[] keyValues)
  where TEntity : class
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        return await context.FindAsync<TEntity>(keyValues);
    }

    public async Task<TEntity?> FindOneAsync<TEntity>(Expression<Func<TEntity, bool>> predicate) where TEntity : class
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        return await context.Set<TEntity>().SingleOrDefaultAsync(predicate);
    }

    public static (string? token, DateTime validTo) GenerateToken(User user)
    {
        //var tokenService = CurrentScope.ServiceProvider.GetRequiredService<ITokenService>();
        //return tokenService.GenerateAccessToken(user);
        return (null, DateTime.UtcNow.AddMinutes(30)); // Placeholder for token generation
    }

    public async Task<ClientBuilder> GetWorkflowOutputs(string userId)
    {
        (string? token, DateTime validTo) token = GenerateToken(new User()
        {
            Id = userId,
            OrganizationId = "Inia",
            Email = "TestEmail",
            UserName = "TestUsername",
            FirstName = "Test",
            LastName = "User"
        });

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.token);

        HttpResponseMessage response = await Client.GetAsync("api/v2/workflow-outputs");
        _ = response.EnsureSuccessStatusCode();

        GetWorkflowOutputsResponse = await response.Content.ReadFromJsonAsync<List<WorkflowExecution>>();
        return this;
    }

    public async Task<ClientBuilder> GetVersion()
    {
        HttpResponseMessage response = await Client.GetAsync("api/v1/meta/version");
        _ = response.EnsureSuccessStatusCode();

        GetMetaVersionResponse = await response.Content.ReadFromJsonAsync<GetMetaVersionResponse>();
        return this;
    }

    public async Task<ClientBuilder> GetHealthchecks()
    {
        HttpResponseMessage response = await Client.GetAsync("health");
        _ = response.EnsureSuccessStatusCode();

        return this;
    }
}
