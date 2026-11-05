namespace IniaAi.WebApi.Attributes;

/// <summary>
/// Indicates that an authenticated endpoint does not require a tenant context (X-Tenant-Id header).
/// Use this attribute for tenant-agnostic endpoints like /users/me/tenants that are authenticated
/// but don't operate within a specific tenant context.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TenantOptionalAttribute : Attribute
{
}
