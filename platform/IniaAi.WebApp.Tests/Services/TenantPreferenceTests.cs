using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class TenantPreferenceTests
{
    private static readonly string[] _accessible = ["org-a", "org-b", "org-c"];

    [Fact]
    public void ShouldRestore_WithNoStoredValue_IsFalse()
    {
        Assert.False(TenantPreference.ShouldRestore(null, "org-a", _accessible));
    }

    [Fact]
    public void ShouldRestore_WithEmptyStoredValue_IsFalse()
    {
        Assert.False(TenantPreference.ShouldRestore("", "org-a", _accessible));
    }

    [Fact]
    public void ShouldRestore_WhenStoredEqualsCurrent_IsFalse()
    {
        Assert.False(TenantPreference.ShouldRestore("org-a", "org-a", _accessible));
    }

    [Fact]
    public void ShouldRestore_WhenStoredNotAccessible_IsFalse()
    {
        Assert.False(TenantPreference.ShouldRestore("org-x", "org-a", _accessible));
    }

    [Fact]
    public void ShouldRestore_WhenStoredAccessibleAndDifferent_IsTrue()
    {
        Assert.True(TenantPreference.ShouldRestore("org-b", "org-a", _accessible));
    }

    [Fact]
    public void ShouldRestore_WhenCurrentIsNullAndStoredAccessible_IsTrue()
    {
        Assert.True(TenantPreference.ShouldRestore("org-b", null, _accessible));
    }
}
