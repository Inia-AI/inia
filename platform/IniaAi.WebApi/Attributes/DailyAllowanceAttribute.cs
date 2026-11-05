using IniaAi.Application.Common.DailyAllowances;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Domain.Entities.DailyAllowances;
using IniaAi.WebApi.Filters;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IniaAi.WebApi.Attributes;

/// <summary>
/// Each request to the endpoint counts against the user's daily allowance of this kind
/// (<see cref="IDailyAllowance"/>), through a <see cref="DailyAllowanceFilter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DailyAllowanceAttribute(DailyAllowanceKind kind) : Attribute, IFilterFactory
{
    /// <summary>The kind of request the endpoint counts as.</summary>
    public DailyAllowanceKind Kind { get; } = kind;

    /// <inheritdoc />
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        return new DailyAllowanceFilter(
            Kind,
            serviceProvider.GetRequiredService<IDailyAllowance>(),
            serviceProvider.GetRequiredService<IRequestContext>(),
            serviceProvider.GetRequiredService<IRequestModelUse>(),
            serviceProvider.GetRequiredService<ILogger<DailyAllowanceFilter>>(),
            serviceProvider.GetService<TimeProvider>());
    }
}
