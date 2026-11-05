using IniaAi.Common.Hosting;

namespace IniaAi.Common.Tests.Hosting;

public class UnixAccountTests
{
    [Fact]
    public void Find_Root_ReadsItsIdsAndHomeFromThePasswordFile()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no password file.");

        UnixAccount? root = UnixAccount.Find("root");

        Assert.NotNull(root);
        Assert.Equal(0u, root.UserId);
        Assert.Equal(0u, root.GroupId);
        Assert.StartsWith("/", root.Home, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_AUserTheHostDoesNotHave_IsNull()
    {
        Assert.Null(UnixAccount.Find("no-such-user-on-this-host"));
    }

    [Fact]
    public void ToStartChildrenAs_FromAProcessThatIsNotRoot_IsNull()
    {
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "This case needs a process that is not root.");

        Assert.Null(UnixAccount.ToStartChildrenAs("root"));
    }
}
