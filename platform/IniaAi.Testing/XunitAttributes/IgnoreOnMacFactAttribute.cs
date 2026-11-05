using Xunit;

namespace IniaAi.Testing.XunitAttributes;

public class IgnoreOnMacFactAttribute : FactAttribute
{
    public IgnoreOnMacFactAttribute(string? reason = null, bool cicdOnly = false)
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
