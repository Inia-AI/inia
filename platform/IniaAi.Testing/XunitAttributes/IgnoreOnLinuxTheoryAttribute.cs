using Xunit;

namespace IniaAi.Testing.XunitAttributes;

public class IgnoreOnLinuxTheoryAttribute : TheoryAttribute
{
    public IgnoreOnLinuxTheoryAttribute(string? reason = null, bool cicdOnly = false)
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
