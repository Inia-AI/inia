using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Shared.Users;
using System.Linq.Expressions;

namespace IniaAi.Application.Queries.Users;

/// <summary>
/// <see cref="User.GetFullName"/> runs after the database returns the row, so a list cannot
/// filter or sort by <see cref="UserDto.FullName"/>.
/// </summary>
public static class UserProjections
{
    public static readonly Expression<Func<User, UserDto>> ToDto = user => new UserDto
    {
        Id = user.Id,
        Email = user.Email!,
        Username = user.UserName!,
        FirstName = user.FirstName,
        LastName = user.LastName,
        MiddleName = user.MiddleName,
        FullName = user.GetFullName(),
    };
}
