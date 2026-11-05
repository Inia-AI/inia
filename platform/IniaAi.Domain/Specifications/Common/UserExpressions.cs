using IniaAi.Domain.Entities.UserManagement;
using System.Linq.Expressions;

namespace IniaAi.Domain.Specifications.Common;

/// <summary>
/// Reusable expression library for User entity filtering.
/// Provides common user query predicates that can be reused across specifications.
/// </summary>
public static class UserExpressions
{
    /// <summary>
    /// Expression for filtering users who can currently authenticate (not locked out — transient or permanent).
    /// Distinct from <see cref="User.IsDisabled"/>, which flags only the permanent administrative-disable sentinel.
    /// </summary>
    public static Expression<Func<User, bool>> IsActive =>
        u => !u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow;

    /// <summary>
    /// Expression for filtering non-system users.
    /// </summary>
    public static Expression<Func<User, bool>> IsNotSystem =>
        u => !u.IsSystem;

    /// <summary>
    /// Expression for filtering by organization ID.
    /// </summary>
    public static Expression<Func<User, bool>> ByOrganization(string organizationId)
    {
        return u => u.OrganizationId == organizationId;
    }

    /// <summary>
    /// Expression for filtering by user ID.
    /// </summary>
    public static Expression<Func<User, bool>> ById(string id)
    {
        return u => u.Id == id;
    }

    /// <summary>
    /// Expression for filtering by email address.
    /// </summary>
    public static Expression<Func<User, bool>> ByEmail(string email)
    {
        return u => u.Email == email;
    }

    /// <summary>
    /// Expression for filtering users by username (contains search).
    /// </summary>
    public static Expression<Func<User, bool>> ByUsernameContains(string searchTerm)
    {
        return u => u.UserName != null && u.UserName.ToLower().Contains(searchTerm.ToLower());
    }

    /// <summary>
    /// Expression for filtering users by email domain.
    /// </summary>
    public static Expression<Func<User, bool>> ByEmailDomain(string domain)
    {
        return u => u.Email != null && u.Email.ToLower().EndsWith(domain.ToLower());
    }
}
