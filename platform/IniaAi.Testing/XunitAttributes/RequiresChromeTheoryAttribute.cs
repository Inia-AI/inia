using Xunit;

namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// A theory whose cases start Google Chrome. CI skips it, and off CI it skips when Chrome is not
/// installed.
/// </summary>
public sealed class RequiresChromeTheoryAttribute : TheoryAttribute
{
    public RequiresChromeTheoryAttribute()
    {
        Skip = Chrome.SkipReason();
    }
}
