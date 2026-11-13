using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// The work factor of every stored password hash: the OWASP Password Storage Cheat Sheet's 220,000
/// iterations for PBKDF2-HMAC-SHA512, the function Identity's hasher uses. Both apps' Identity takes it,
/// and so does every hash made outside Identity, so a hash check takes as long on every path. Identity
/// hashes a password again at its user's next sign-in when its stored hash has fewer iterations.
/// </summary>
public static class PasswordHashing
{
    public const int ITERATIONS = 220_000;

    public static PasswordHasher<User> Hasher { get; } = new(Options.Create(new PasswordHasherOptions { IterationCount = ITERATIONS }));

    public static void Apply(PasswordHasherOptions options)
    {
        options.IterationCount = ITERATIONS;
    }
}
