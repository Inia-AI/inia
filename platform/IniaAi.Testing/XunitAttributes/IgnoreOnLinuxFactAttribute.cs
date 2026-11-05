using Xunit;

namespace IniaAi.Testing.XunitAttributes;

public class IgnoreOnLinuxFactAttribute : FactAttribute
{
    public IgnoreOnLinuxFactAttribute(string? reason = null, bool cicdOnly = false)
    {
        if (!Utils.IsRunningOnLinux())
        {
            return;
        }

        if (cicdOnly && !Utils.IsRunningOnCI())
        {
            return;
        }

        Skip = reason ?? "This test is ignored on Linux.";
    }
}
