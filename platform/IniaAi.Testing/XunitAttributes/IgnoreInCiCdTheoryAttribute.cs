using Xunit;

namespace IniaAi.Testing.XunitAttributes;

public class IgnoreInCiCdTheoryAttribute : TheoryAttribute
{
    public IgnoreInCiCdTheoryAttribute(string? reason = null)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null)
        {
            Skip = $"Ignored on GitHub Actions{(reason is not null ? $": {reason}" : string.Empty)}";
        }
        else if (Environment.GetEnvironmentVariable("SYSTEM_DEFINITIONID") is not null)
        {
            Skip = $"Ignored on Azure DevOps{(reason is not null ? $": {reason}" : string.Empty)}";
        }
        else if (Environment.GetEnvironmentVariable("TEAMCITY_VERSION") is not null)
        {
            Skip = $"Ignored on TeamCity{(reason is not null ? $": {reason}" : string.Empty)}";
        }
    }
}
