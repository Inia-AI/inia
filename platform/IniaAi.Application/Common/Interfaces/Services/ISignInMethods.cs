using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Hears every sign-in just before the token endpoint issues its tokens, with the grant it used, so
/// a module can act on the method: record it, or end the account's other sessions the first time a
/// new method signs in. A refresh is not a sign-in and is not heard. The default does nothing.
/// </summary>
public interface ISignInMethods
{
    Task SigningInAsync(User user, string method, CancellationToken cancellationToken);
}
