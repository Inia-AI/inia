using System.IO.Compression;
using System.Reflection;
using IniaAi.Common.Resources;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// The passwords of at least <see cref="PasswordPolicy.MINIMUM_LENGTH"/> characters among the UK
/// National Cyber Security Centre's 100,000 most used ones, lower-cased: SecLists
/// <c>Passwords/Common-Credentials/100k-most-used-passwords-NCSC.txt</c> (MIT licence), in
/// <c>CommonPasswords.txt.gz</c>. They are read once, on first use, and compared in any case.
/// </summary>
internal static class CommonPasswords
{
    private const string RESOURCE = "CommonPasswords.txt.gz";

    private static readonly Lazy<HashSet<string>> _passwords = new(Load);

    public static int Count => _passwords.Value.Count;

    public static bool Contains(string password)
    {
        return _passwords.Value.Contains(password);
    }

    private static HashSet<string> Load()
    {
        using Stream compressed = Assembly.GetExecutingAssembly().ReadResource(RESOURCE);
        using GZipStream lines = new(compressed, CompressionMode.Decompress);
        using StreamReader reader = new(lines);

        HashSet<string> passwords = new(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                _ = passwords.Add(line);
            }
        }

        return passwords;
    }
}
