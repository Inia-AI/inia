using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IniaAi.WebApi.Attributes;

/// <summary>
/// A user in one of the staff roles passes; anyone else passes only when their organization has the
/// capability, and is refused as <see cref="RequiresCapabilityAttribute"/> refuses. The endpoint then
/// limits a caller outside the staff roles to what the capability allows.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class StaffOrCapabilityAttribute(string staffRoles, string capability) : Attribute, IFilterFactory
{
    /// <summary>The roles that pass without the capability, comma-separated.</summary>
    public string StaffRoles { get; } = staffRoles;

    /// <summary>The capability that lets anyone else pass.</summary>
    public string Capability { get; } = capability;

    /// <inheritdoc />
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var gate = (IAsyncAuthorizationFilter)new RequiresCapabilityAttribute(Capability).CreateInstance(serviceProvider);
        return new StaffOrGate(StaffRoles, gate);
    }

    private sealed class StaffOrGate(string staffRoles, IAsyncAuthorizationFilter gate) : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            return Roles.HeldBy(context.HttpContext.User, staffRoles) ? Task.CompletedTask : gate.OnAuthorizationAsync(context);
        }
    }
}
