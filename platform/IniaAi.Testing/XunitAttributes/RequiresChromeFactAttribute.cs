using Xunit;

namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// A test that starts Google Chrome. CI skips it, and off CI it skips when Chrome is not installed.
/// </summary>
public sealed class RequiresChromeFactAttribute : FactAttribute
{
    public RequiresChromeFactAttribute()
    {
        Skip = Chrome.SkipReason();
    }
}
