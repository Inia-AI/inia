using System.Buffers.Binary;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IniaAi.Domain.Tests.UserManagement;

/// <summary>
/// Every stored password hash takes the work factor the OWASP Password Storage Cheat Sheet gives for
/// PBKDF2-HMAC-SHA512, the function Identity's hasher uses: 220,000 iterations. A hash made with fewer
/// is upgraded when its user next signs in.
/// </summary>
public class PasswordHashingTests
{
    private const string PASSWORD = "Abcdefg1";
    private const int OWASP_PBKDF2_SHA512_ITERATIONS = 220_000;
    private const int IDENTITY_DEFAULT_ITERATIONS = 100_000;

    [Fact]
    public void APasswordSetOnTheUser_IsHashedWithTheOwaspWorkFactor()
    {
        var user = new User();

        user.SetPassword(PASSWORD);

        Assert.Equal(OWASP_PBKDF2_SHA512_ITERATIONS, IterationsOf(user.PasswordHash!));
    }

    [Fact]
    public void TheSharedHasher_UsesTheOwaspWorkFactor()
    {
        Assert.Equal(OWASP_PBKDF2_SHA512_ITERATIONS, IterationsOf(PasswordHashing.Hasher.HashPassword(new User(), PASSWORD)));
    }

    [Fact]
    public void AHashMadeWithFewerIterations_AsksToBeRehashed()
    {
        var user = new User();
        string oldHash = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = IDENTITY_DEFAULT_ITERATIONS }))
            .HashPassword(user, PASSWORD);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, PasswordHashing.Hasher.VerifyHashedPassword(user, oldHash, PASSWORD));
    }

    [Fact]
    public void ApplyingTheWorkFactor_SetsIdentitysOptions()
    {
        var options = new PasswordHasherOptions();

        PasswordHashing.Apply(options);

        Assert.Equal(OWASP_PBKDF2_SHA512_ITERATIONS, options.IterationCount);
    }

    /// <summary>Identity's version 3 format: a marker byte, the PRF and the iteration count as big-endian integers, then the salt and the key.</summary>
    private static int IterationsOf(string hash)
    {
        byte[] bytes = Convert.FromBase64String(hash);
        Assert.Equal(0x01, bytes[0]);

        return (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4));
    }
}
