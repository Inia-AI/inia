using Xunit;

namespace IniaAi.Testing;

/// <summary>
/// Spellings of an address that ASP.NET Core Identity's default lookup normalizer (Unicode NFC,
/// then invariant upper case) folds onto the address itself, so looking an account up by any of
/// them finds the account. The long s upper-cases to S, and NFC turns the Kelvin sign into K. The
/// dotless i is not one: .NET's invariant casing keeps it as it is.
/// </summary>
public static class EmailLookalikes
{
    private const char LONG_S = '\u017F';
    private const char KELVIN_SIGN = '\u212A';

    /// <summary>
    /// The <see cref="UpperCase"/>, <see cref="LongS"/> and <see cref="KelvinSign"/> spellings of
    /// <paramref name="address"/>.
    /// </summary>
    public static TheoryData<string> Of(string address)
    {
        return new TheoryData<string> { UpperCase(address), LongS(address), KelvinSign(address) };
    }

    public static string UpperCase(string address)
    {
        return address.ToUpperInvariant();
    }

    /// <summary>
    /// <paramref name="address"/> with each s written as a long s. The address needs an s.
    /// </summary>
    public static string LongS(string address)
    {
        return Respelled(address, 's', LONG_S);
    }

    /// <summary>
    /// <paramref name="address"/> with each k written as a Kelvin sign. The address needs a k.
    /// </summary>
    public static string KelvinSign(string address)
    {
        return Respelled(address, 'k', KELVIN_SIGN);
    }

    private static string Respelled(string address, char letter, char lookalike)
    {
        string respelled = address.Replace(letter, lookalike);
        if (respelled == address)
        {
            throw new ArgumentException($"The address has no '{letter}' to write as U+{(int)lookalike:X4}.", nameof(address));
        }

        return respelled;
    }
}
