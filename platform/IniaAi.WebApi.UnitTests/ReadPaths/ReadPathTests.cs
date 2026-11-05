using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Services;
using IniaAi.WebApi.UnitTests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.UnitTests.ReadPaths;

/// <summary>
/// A handler that reads one record or one export, or answers a write with the record it saved.
/// </summary>
public abstract class ReadPathTests : FixtureHandlerTests
{
    protected override string FixtureFolder => "IniaAi.WebApi.UnitTests/ReadPaths/Fixtures";

    protected static readonly DateTime Created = new(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);

    protected override void ConfigureServices(IServiceCollection services)
    {
        _ = services.AddScoped<ICoreDbContext>(provider => provider.GetRequiredService<CoreDbContext>());
        _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
        _ = services.AddSingleton<IDbExceptionAnalyzer, DbExceptionAnalyzer>();
    }
}
