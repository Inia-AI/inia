using Xunit;

namespace IniaAi.Testing.XunitAttributes;

public class IgnoreOnWindowsFactAttribute : FactAttribute
{
    public IgnoreOnWindowsFactAttribute(string? reason = null, bool cicdOnly = false)
    {
        if (!Utils.IsRunningOnWindows())
        {
            return;
        }

        if (cicdOnly && !Utils.IsRunningOnCI())
        {
            return;
        }

        Skip = reason ?? "This test is ignored on Windows.";
    }
}
