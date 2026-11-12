using System.Security.Cryptography;

namespace IniaAi.Testing;

/// <summary>
/// The code an authenticator app shows now for a key that ASP.NET Core Identity issued (RFC 6238:
/// SHA-1, 30-second steps, six digits), so a test signs in with a second factor as a user would.
/// </summary>
public static class AuthenticatorCodes
{
    private const long STEP_SECONDS = 30;
    private const int SIX_DIGIT_MODULUS = 1_000_000;

    public static string Current(string base32Key)
    {
        byte[] key = Base32Decode(base32Key);
        long step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / STEP_SECONDS;
        byte[] counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        byte[] hash = HMACSHA1.HashData(key, counter);
        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % SIX_DIGIT_MODULUS).ToString("D6");
    }

    private static byte[] Base32Decode(string text)
    {
        const string ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = [];
        int buffer = 0;
        int bits = 0;

        foreach (char letter in text.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | ALPHABET.IndexOf(letter);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. bytes];
    }
}
