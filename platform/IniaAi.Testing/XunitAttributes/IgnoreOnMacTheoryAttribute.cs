using Xunit;

namespace IniaAi.Testing.XunitAttributes;

public class IgnoreOnMacTheoryAttribute : TheoryAttribute
{
    public IgnoreOnMacTheoryAttribute(string? reason = null, bool cicdOnly = false)
    {
        if (!Utils.IsRunningOnMac())
        {
            return;
        }

        if (cicdOnly && !Utils.IsRunningOnCI())
        {
            return;
        }

        Skip = reason ?? "This test is ignored on macOS.";
    }
}
