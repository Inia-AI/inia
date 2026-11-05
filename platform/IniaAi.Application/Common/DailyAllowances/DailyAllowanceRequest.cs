using System.Security.Claims;
using IniaAi.Domain.Entities.DailyAllowances;

namespace IniaAi.Application.Common.DailyAllowances;

/// <param name="User">The signed-in user, so a limit can leave out the platform's staff.</param>
public sealed record DailyAllowanceRequest(DailyAllowanceKind Kind, string OrganizationId, string UserId, ClaimsPrincipal User);
