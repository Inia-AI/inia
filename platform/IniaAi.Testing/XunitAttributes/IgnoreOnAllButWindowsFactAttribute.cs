using Xunit;

namespace IniaAi.Testing.XunitAttributes;

public class IgnoreOnAllButWindowsFactAttribute : FactAttribute
{
    public IgnoreOnAllButWindowsFactAttribute(string? reason = null, bool cicdOnly = false)
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
