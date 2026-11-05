using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.Signatures;

/// <summary>
/// Batched user-id to email lookup used by audit-write paths (signature override promotion,
/// document unlock) to denormalize actor emails — e.g.
/// <see cref="SignatureFieldOverrideAuditLog.ActorUserEmail"/> — onto each audit row at write
/// time. Single <c>WHERE Id IN (...)</c> query per call so the audit emission stays cheap
/// regardless of how many actors are involved.
/// </summary>
public static class SignatureActorResolver
{
    public static async Task<Dictionary<string, string?>> ResolveEmailsAsync(
        IUserDataUnitOfWork userDataUnitOfWork,
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userDataUnitOfWork);
        ArgumentNullException.ThrowIfNull(userIds);

        Dictionary<string, string?> result = [];
        if (userIds.Count == 0)
        {
            return result;
        }

        HashSet<string> distinct = [];
        foreach (string id in userIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                _ = distinct.Add(id);
            }
        }

        if (distinct.Count == 0)
        {
            return result;
        }

        IRepository<User> repo = userDataUnitOfWork.Repository<User>();

        List<User> users = await repo
            .GetAll()
            .AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .ToListAsync(cancellationToken);

        foreach (User user in users)
        {
            result[user.Id] = user.Email;
        }

        return result;
    }
}
