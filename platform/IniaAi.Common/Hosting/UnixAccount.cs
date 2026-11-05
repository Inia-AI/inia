using System.Globalization;

namespace IniaAi.Common.Hosting;

/// <summary>
/// A user of this host, as its password file names it. A process that runs as root starts a child as such a user, so
/// the child cannot read root's files.
/// </summary>
public sealed record UnixAccount(string Name, uint UserId, uint GroupId, string Home)
{
    private const string PASSWD_FILE = "/etc/passwd";

    /// <summary>The account named <paramref name="name"/>, or null when this host has no password file or no such user.</summary>
    public static UnixAccount? Find(string name)
    {
        if (!File.Exists(PASSWD_FILE))
        {
            return null;
        }

        foreach (string line in File.ReadLines(PASSWD_FILE))
        {
            string[] fields = line.Split(':');
            if (fields.Length > 5 && fields[0] == name
                && uint.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint userId)
                && uint.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint groupId))
            {
                return new UnixAccount(name, userId, groupId, fields[5]);
            }
        }

        return null;
    }

    /// <summary>
    /// The account named <paramref name="name"/> when this process runs as root on Linux and the host has that user;
    /// otherwise null, and a child runs as this process's own user.
    /// </summary>
    public static UnixAccount? ToStartChildrenAs(string name)
    {
        return OperatingSystem.IsLinux() && Environment.IsPrivilegedProcess ? Find(name) : null;
    }
}
