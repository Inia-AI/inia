using Xunit;

namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// A test too slow for every pull request. CI runs it in its nightly run only, where it may take longer
/// than <see cref="TestTimeCeilingAttribute.Ceiling"/>; a local run always runs it. The reason says
/// what makes it slow.
/// </summary>
public sealed class NightlyFactAttribute : FactAttribute
{
    public NightlyFactAttribute(string reason)
    {
        Skip = NightlyOnly.SkipReason(reason);
    }
}
