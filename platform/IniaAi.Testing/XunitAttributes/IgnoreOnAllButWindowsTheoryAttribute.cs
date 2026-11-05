using Xunit;

namespace IniaAi.Testing.XunitAttributes;

public class IgnoreOnAllButWindowsTheoryAttribute : TheoryAttribute
{
    public IgnoreOnAllButWindowsTheoryAttribute(string? reason = null, bool cicdOnly = false)
    {
        if (Utils.IsRunningOnWindows())
        {
            return;
        }

        if (cicdOnly && !Utils.IsRunningOnCI())
        {
            return;
        }

        Skip = reason ?? "This test is ignored on all platforms except Windows.";
    }
}
