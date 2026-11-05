using IniaAi.Configuration.Platform;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Configuration.Tests.Platform;

public class AgentRuntimeSettingsTests
{
    private static List<ValidationResult> Validate(AgentRuntimeSettings settings)
    {
        var results = new List<ValidationResult>();
        _ = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    private static AgentRuntimeSettings CreateEnabledSettings()
    {
        return new AgentRuntimeSettings
        {
            Enabled = true,
            BaseUrl = "http://localhost:8130",
            AuthToken = "configured-elsewhere",
            AllowedModels = ["test-model-a", "test-model-b"],
        };
    }

    [Fact]
    public void SectionName_IsPlatformSettingsAgentRuntime()
    {
        Assert.Equal("PlatformSettings:AgentRuntime", AgentRuntimeSettings.SectionName);
    }

    [Fact]
    public void Defaults_AreDisabledAndValid()
    {
        var settings = new AgentRuntimeSettings();

        Assert.False(settings.Enabled);
        Assert.False(settings.AllowUnauthenticatedSidecar);
        Assert.Equal(string.Empty, settings.AuthToken);
        Assert.Equal(30, settings.TimeoutSeconds);
        Assert.Equal(24, settings.RunTokenValidityHours);
        Assert.Empty(Validate(settings));
    }

    [Fact]
    public void Disabled_SkipsAllValidation()
    {
        var settings = new AgentRuntimeSettings
        {
            Enabled = false,
            BaseUrl = string.Empty,
            AuthToken = string.Empty,
            AllowedModels = [],
        };

        Assert.Empty(Validate(settings));
    }

    [Fact]
    public void Enabled_FullyConfigured_IsValid()
    {
        Assert.Empty(Validate(CreateEnabledSettings()));
    }

    [Fact]
    public void Enabled_EmptyAllowedModels_FailsValidation()
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.AllowedModels = [];

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.AllowedModels), result.MemberNames);
    }

    [Fact]
    public void Enabled_NullAllowedModels_FailsValidationInsteadOfThrowing()
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.AllowedModels = null!;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.AllowedModels), result.MemberNames);
    }

    [Fact]
    public void Enabled_BlankAllowedModelEntry_FailsValidation()
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.AllowedModels = ["test-model-a", "   "];

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.AllowedModels), result.MemberNames);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("not a url")]
    public void Enabled_InvalidBaseUrl_FailsValidation(string baseUrl)
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.BaseUrl = baseUrl;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.BaseUrl), result.MemberNames);
    }

    [Theory]
    [InlineData("http://localhost:8130/v1")]
    [InlineData("http://localhost:8130/v1/")]
    [InlineData("http://localhost:8130/agent/v2")]
    public void Enabled_BaseUrlCarryingTheApiVersion_FailsValidation(string baseUrl)
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.BaseUrl = baseUrl;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.BaseUrl), result.MemberNames);
    }

    [Theory]
    [InlineData("http://localhost:8130")]
    [InlineData("http://localhost:8130/")]
    [InlineData("https://agent-runtime.internal/agent")]
    [InlineData("https://agent-runtime.internal/venue")]
    public void Enabled_BaseUrlWithoutTheApiVersion_IsValid(string baseUrl)
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.BaseUrl = baseUrl;

        Assert.Empty(Validate(settings));
    }

    [Fact]
    public void Enabled_MissingAuthToken_FailsValidation()
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.AuthToken = string.Empty;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.AuthToken), result.MemberNames);
    }

    [Fact]
    public void Enabled_MissingAuthTokenWithExplicitLocalOptOut_IsValid()
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.AuthToken = string.Empty;
        settings.AllowUnauthenticatedSidecar = true;

        Assert.Empty(Validate(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveTimeoutSeconds_Fails(int timeoutSeconds)
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.TimeoutSeconds = timeoutSeconds;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.TimeoutSeconds), result.MemberNames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveRunTokenValidityHours_Fails(int runTokenValidityHours)
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.RunTokenValidityHours = runTokenValidityHours;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.RunTokenValidityHours), result.MemberNames);
    }

    [Fact]
    public void PatienceDefaults_OutlastAShedAndAllowRetries()
    {
        var settings = new AgentRuntimeSettings();

        Assert.Equal(600, settings.AdmissionPatienceSeconds);
        Assert.Equal(3, settings.MaxRunPasses);
        Assert.Equal(3, settings.PollErrorTolerance);
        Assert.Equal(120, settings.RunDeadlineMarginSeconds);
        Assert.Equal(30, settings.MaxPollDeadlineMinutes);
        Assert.Equal(600, settings.UpstreamErrorRetryDelaySeconds);
    }

    [Fact]
    public void NegativeUpstreamErrorRetryDelay_Fails()
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.UpstreamErrorRetryDelaySeconds = -1;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.UpstreamErrorRetryDelaySeconds), result.MemberNames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveMaxPollDeadlineMinutes_Fails(int maxPollDeadlineMinutes)
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.MaxPollDeadlineMinutes = maxPollDeadlineMinutes;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.MaxPollDeadlineMinutes), result.MemberNames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveMaxRunPasses_Fails(int maxRunPasses)
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.MaxRunPasses = maxRunPasses;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.MaxRunPasses), result.MemberNames);
    }

    [Fact]
    public void ZeroAdmissionPatienceAndPollTolerance_AreValid()
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.AdmissionPatienceSeconds = 0;
        settings.PollErrorTolerance = 0;
        settings.RunDeadlineMarginSeconds = 0;
        settings.UpstreamErrorRetryDelaySeconds = 0;

        Assert.Empty(Validate(settings));
    }

    [Fact]
    public void McpServerUrl_DefaultsToNullAndValidates()
    {
        Assert.Null(new AgentRuntimeSettings().McpServerUrl);
        Assert.Empty(Validate(CreateEnabledSettings()));
    }

    [Fact]
    public void AbsoluteMcpServerUrl_Passes()
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.McpServerUrl = "https://agent.example.com/mcp";

        Assert.Empty(Validate(settings));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public void NonAbsoluteMcpServerUrl_Fails(string mcpServerUrl)
    {
        AgentRuntimeSettings settings = CreateEnabledSettings();
        settings.McpServerUrl = mcpServerUrl;

        ValidationResult result = Assert.Single(Validate(settings));
        Assert.Contains(nameof(AgentRuntimeSettings.McpServerUrl), result.MemberNames);
    }
}
