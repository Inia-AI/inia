using IniaAi.Domain.Entities.Tools;
using System.Linq.Expressions;

namespace IniaAi.Domain.Specifications.Common;

/// <summary>
/// Reusable expression library for <see cref="RefinementPreset"/> filtering.
/// </summary>
public static class RefinementPresetExpressions
{
    /// <summary>
    /// The presets a user of an organization can see: the user's own and the organization's shared ones, not deleted.
    /// </summary>
    public static Expression<Func<RefinementPreset, bool>> VisibleTo(string organizationId, string userId)
    {
        return p => !p.IsDeleted && p.OrganizationId == organizationId && (p.UserId == userId || p.IsShared);
    }
}
