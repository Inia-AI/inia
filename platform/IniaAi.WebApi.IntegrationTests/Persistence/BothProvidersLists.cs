using ErrorOr;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.EntityFrameworkCore;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

// Every OData list projects its entities with a hand-written expression and lets the database
// filter, sort, page and count over that projection. A projection one provider translates and the
// other refuses would only show on a customer's database, and the in-memory provider that runs the
// list tests in CI translates nothing. Each list guard seeds a throwaway database on a real provider
// and runs its lists with $filter, $orderby, $top, $skip and $count through the real handlers.
internal static class BothProvidersLists
{
    public const string ORGANIZATION_ID = "org-lists";
    public const string USER_ID = "user-lists";
    public const string PAGE_TWO_COUNTED = "&$top=1&$skip=1&$count=true";

    public static async Task RunAsync(
        string provider,
        string databasePrefix,
        string guardName,
        Func<UserDataDbContext, CancellationToken, Task> seed,
        Func<ServiceProvider, CancellationToken, Task> assertLists,
        ICoreDatabaseFixture? core = null,
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure = null)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<UserDataDbContext> userDataOptions = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(
            provider, databasePrefix, guardName, cancellationToken);

        try
        {
            await using (UserDataDbContext setup = new(userDataOptions))
            {
                await setup.Database.MigrateAsync(cancellationToken);
                _ = setup.Organizations.Add(new Organization("Lists Org", ORGANIZATION_ID));
                _ = setup.Users.Add(new User(ORGANIZATION_ID) { Id = USER_ID, UserName = "ada@example.com", Email = "ada@example.com", FirstName = "Ada", LastName = "Lovelace" });
                _ = await setup.SaveChangesAsync(cancellationToken);
                await seed(setup, cancellationToken);
                _ = await setup.SaveChangesAsync(cancellationToken);
            }

            await using ServiceProvider services = BothProvidersHandlers.BuildServices(userDataOptions, core, configure);
            await assertLists(services, cancellationToken);
        }
        finally
        {
            await using UserDataDbContext cleanup = new(userDataOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    public static Task<ODataPaginatedResponse<TDto>> ListAsync<THandler, TQuery, TDto>(
        ServiceProvider services,
        string query,
        Func<ODataQueryOptions<TDto>, TQuery> buildQuery,
        Func<THandler, TQuery, Task<ErrorOr<ODataPaginatedResponse<TDto>>>> handle)
        where TDto : class
    {
        return BothProvidersHandlers.HandleAsync<THandler, ODataPaginatedResponse<TDto>>(
            services, handler => handle(handler, buildQuery(ODataTestOptions.For<TDto>(query))));
    }

    public static void AssertPage<TDto>(ODataPaginatedResponse<TDto> page, string[] expectedIds, long expectedCount, string list)
        where TDto : notnull
    {
        List<string> ids = [.. page.Value.Select(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)!)];
        Assert.True(expectedIds.SequenceEqual(ids), $"The {list} list returned [{string.Join(", ", ids)}] instead of [{string.Join(", ", expectedIds)}].");
        Assert.True(expectedCount == page.Count, $"The {list} list counted {page.Count} instead of {expectedCount}.");
    }
}
