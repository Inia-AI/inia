using IniaAi.Configuration.Platform;

namespace IniaAi.Configuration.Tests;

public class ModelRoutingSettingsModelOverrideTests
{
    [Fact]
    public void GetModelOverride_NoOverridesConfigured_ReturnsNull()
    {
        var settings = new ModelRoutingSettings();

        string? result = settings.GetModelOverride("tenant-1", shouldUseAdvancedModel: true);

        Assert.Null(result);
    }

    [Fact]
    public void GetModelOverride_TenantNotInOverrides_ReturnsNull()
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["other-tenant"] = new() { AdvancedModel = "claude-opus-4-6" }
            }
        };

        string? result = settings.GetModelOverride("tenant-1", shouldUseAdvancedModel: true);

        Assert.Null(result);
    }

    [Fact]
    public void GetModelOverride_MatchingTenant_AdvancedModel_ReturnsAdvancedModel()
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["tenant-1"] = new()
                {
                    AdvancedModel = "claude-opus-4-6",
                    BasicModel = "claude-haiku-4-5"
                }
            }
        };

        string? result = settings.GetModelOverride("tenant-1", shouldUseAdvancedModel: true);

        Assert.Equal("claude-opus-4-6", result);
    }

    [Fact]
    public void GetModelOverride_MatchingTenant_BasicModel_ReturnsBasicModel()
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["tenant-1"] = new()
                {
                    AdvancedModel = "claude-opus-4-6",
                    BasicModel = "claude-haiku-4-5"
                }
            }
        };

        string? result = settings.GetModelOverride("tenant-1", shouldUseAdvancedModel: false);

        Assert.Equal("claude-haiku-4-5", result);
    }

    [Fact]
    public void GetModelOverride_OverrideExistsButAdvancedModelIsNull_ReturnsNull()
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["tenant-1"] = new() { BasicModel = "claude-haiku-4-5" }
            }
        };

        string? result = settings.GetModelOverride("tenant-1", shouldUseAdvancedModel: true);

        Assert.Null(result);
    }

    [Fact]
    public void GetModelOverride_OverrideExistsButBasicModelIsNull_ReturnsNull()
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["tenant-1"] = new() { AdvancedModel = "claude-opus-4-6" }
            }
        };

        string? result = settings.GetModelOverride("tenant-1", shouldUseAdvancedModel: false);

        Assert.Null(result);
    }

    [Fact]
    public void GetModelOverride_CaseInsensitiveTenantIdLookup()
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["Tenant-1"] = new() { AdvancedModel = "claude-opus-4-6" }
            }
        };

        Assert.Equal("claude-opus-4-6", settings.GetModelOverride("tenant-1", shouldUseAdvancedModel: true));
        Assert.Equal("claude-opus-4-6", settings.GetModelOverride("TENANT-1", shouldUseAdvancedModel: true));
        Assert.Equal("claude-opus-4-6", settings.GetModelOverride("Tenant-1", shouldUseAdvancedModel: true));
    }

    [Fact]
    public void GetModelOverride_NullTenantId_ReturnsNull()
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["tenant-1"] = new() { AdvancedModel = "claude-opus-4-6" }
            }
        };

        string? result = settings.GetModelOverride(null, shouldUseAdvancedModel: true);

        Assert.Null(result);
    }

    [Fact]
    public void GetModelOverride_EmptyTenantId_ReturnsNull()
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["tenant-1"] = new() { AdvancedModel = "claude-opus-4-6" }
            }
        };

        string? result = settings.GetModelOverride("", shouldUseAdvancedModel: true);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(true, "claude-opus-5-5")]
    [InlineData(false, "claude-sonnet-5-5")]
    public void GetAgentModelOverride_MatchingTenantCaseInsensitively_ReturnsTheAgentTier(bool shouldUseAdvancedModel, string expected)
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["Tenant-1"] = new()
                {
                    AdvancedModel = "claude-opus-4-6",
                    BasicModel = "claude-haiku-4-5",
                    AgentAdvancedModel = "claude-opus-5-5",
                    AgentBasicModel = "claude-sonnet-5-5",
                }
            }
        };

        Assert.Equal(expected, settings.GetAgentModelOverride("tenant-1", shouldUseAdvancedModel));
        Assert.Equal(
            shouldUseAdvancedModel ? "claude-opus-4-6" : "claude-haiku-4-5",
            settings.GetModelOverride("tenant-1", shouldUseAdvancedModel));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetAgentModelOverride_BlankAgentTier_ReturnsNull(string? blank)
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["tenant-1"] = new() { AdvancedModel = "claude-opus-4-6", AgentAdvancedModel = blank }
            }
        };

        Assert.Null(settings.GetAgentModelOverride("tenant-1", shouldUseAdvancedModel: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other-tenant")]
    public void GetAgentModelOverride_NoTenantOrNoMatch_ReturnsNull(string? tenantId)
    {
        var settings = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                ["tenant-1"] = new() { AgentAdvancedModel = "claude-opus-5-5" }
            }
        };

        Assert.Null(settings.GetAgentModelOverride(tenantId, shouldUseAdvancedModel: true));
    }
}
