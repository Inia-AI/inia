using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.Configuration.LLMProviders;

public class AnthropicSettings
{
    public string? ApiKey { get; set; }

    public string ApiUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    public string ApiVersion { get; set; } = "2023-06-01";

    public AiModel AdvancedModel { get; set; } = AiModel.PublicSonnet5;

    public string? AdvancedModelIdOverride { get; set; }

    public double AdvancedModelTemperature { get; set; } = 0.2;

    public int AdvancedModelMaxTokens { get; set; } = 64000;

    public AiModel BasicModel { get; set; } = AiModel.PublicHaiku5_5;

    public string? BasicModelIdOverride { get; set; }

    public double BasicModelTemperature { get; set; } = 0.2;

    public int BasicModelMaxTokens { get; set; } = 8192;

    public int MaxRateLimitRetryAttempts { get; set; } = 10;

    public int MaxServiceUnavailableRetryAttempts { get; set; } = 10;

    public int MaxParallelCalls { get; set; } = 5;

    public int MaxInputTokensPerWindow { get; set; } = 1000000;

    public int HttpTimeoutMinutes { get; set; } = 10;
}
