using IniaAi.Common.Attributes;
using IniaAi.Common.Enums;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Configuration.Enums.Anthropic;

public enum AiModel
{
    #region Not Supported Models

    [EnumMember(Value = "claude-sonnet-4-5-20250929")]
    [Description("Claude Sonnet 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    [ThinkingModel]
    PublicSonnet4_5 = 37,

    [EnumMember(Value = "claude-sonnet-4-5-20250929-v1:0")]
    [Description("AWS Bedrock Sonnet 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    AwsSonnet4_5 = 38,

    [EnumMember(Value = "global.anthropic.claude-sonnet-4-5-20250929-v1:0")]
    [Description("AWS Bedrock Global Sonnet 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    [ThinkingModel]
    GlobalAwsSonnet4_5 = 39,

    [EnumMember(Value = "eu.anthropic.claude-sonnet-4-5-20250929-v1:0")]
    [Description("AWS Bedrock EU Sonnet 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    EuAwsSonnet4_5 = 41,

    [EnumMember(Value = "us.anthropic.claude-sonnet-4-5-20250929-v1:0")]
    [Description("AWS Bedrock US Sonnet 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    [ThinkingModel]
    UsAwsSonnet4_5 = 40,

    [EnumMember(Value = "anthropic.claude-haiku-4-5-20251001-v1:0")]
    [Description("AWS Bedrock Haiku 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    AwsHaiku4_5 = 42,

    [EnumMember(Value = "eu.anthropic.claude-haiku-4-5-20251001-v1:0")]
    [Description("AWS Bedrock EU Haiku 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    EuAwsHaiku4_5 = 43,

    [EnumMember(Value = "anthropic.claude-opus-4-5-20251101-v1:0")]
    [Description("AWS Bedrock Opus 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(32000)]
    AwsOpus4_5 = 46,

    [EnumMember(Value = "eu.anthropic.claude-opus-4-5-20251101-v1:0")]
    [Description("AWS Bedrock EU Opus 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(32000)]
    EuAwsOpus4_5 = 47,

    [EnumMember(Value = "claude-opus-4-5-20251101")]
    [Description("Claude Opus 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(32000)]
    [ThinkingModel]
    Opus4_5 = 48,

    [EnumMember(Value = "us.anthropic.claude-opus-4-5-20251101-v1:0")]
    [Description("AWS Bedrock US Opus 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(32000)]
    [ThinkingModel]
    UsAwsOpus4_5 = 49,

    [EnumMember(Value = "anthropic.claude-sonnet-4-6")]
    [Description("AWS Bedrock Sonnet 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    [ThinkingModel]
    AwsSonnet4_6 = 51,

    [EnumMember(Value = "eu.anthropic.claude-sonnet-4-6")]
    [Description("AWS Bedrock EU Sonnet 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    [ThinkingModel]
    EuAwsSonnet4_6 = 52,

    [EnumMember(Value = "global.anthropic.claude-sonnet-4-6")]
    [Description("AWS Bedrock Global Sonnet 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(64000)]
    [ThinkingModel]
    GlobalAwsSonnet4_6 = 53,

    [EnumMember(Value = "claude-opus-4-6")]
    [Description("Claude Opus 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    PublicOpus4_6 = 54,

    [EnumMember(Value = "anthropic.claude-opus-4-6-v1")]
    [Description("AWS Bedrock Opus 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    AwsOpus4_6 = 55,

    [EnumMember(Value = "eu.anthropic.claude-opus-4-6-v1")]
    [Description("AWS Bedrock EU Opus 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    EuAwsOpus4_6 = 56,

    [EnumMember(Value = "us.anthropic.claude-opus-4-6-v1")]
    [Description("AWS Bedrock US Opus 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    UsAwsOpus4_6 = 58,

    [EnumMember(Value = "anthropic.claude-opus-4-8")]
    [Description("AWS Bedrock Opus 4.8")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    AwsOpus4_8 = 60,

    [EnumMember(Value = "eu.anthropic.claude-opus-4-8")]
    [Description("AWS Bedrock EU Opus 4.8")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    EuAwsOpus4_8 = 61,

    [EnumMember(Value = "global.anthropic.claude-opus-4-8")]
    [Description("AWS Bedrock Global Opus 4.8")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    GlobalAwsOpus4_8 = 62,

    [EnumMember(Value = "anthropic.claude-sonnet-5")]
    [Description("AWS Bedrock Sonnet 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    AwsSonnet5 = 64,

    [EnumMember(Value = "eu.anthropic.claude-sonnet-5")]
    [Description("AWS Bedrock EU Sonnet 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    EuAwsSonnet5 = 65,

    [EnumMember(Value = "global.anthropic.claude-sonnet-5")]
    [Description("AWS Bedrock Global Sonnet 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    GlobalAwsSonnet5 = 66,

    [EnumMember(Value = "claude-fable-5")]
    [Description("Claude Fable 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true)]
    PublicFable5 = 67,

    [EnumMember(Value = "anthropic.claude-fable-5")]
    [Description("AWS Bedrock Fable 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true)]
    AwsFable5 = 68,

    [EnumMember(Value = "eu.anthropic.claude-fable-5")]
    [Description("AWS Bedrock EU Fable 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true)]
    EuAwsFable5 = 69,

    [EnumMember(Value = "global.anthropic.claude-fable-5")]
    [Description("AWS Bedrock Global Fable 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true)]
    GlobalAwsFable5 = 70,

    // Inactive until the deployment account opts into Anthropic's 30-day data retention
    // (aws bedrock put-account-data-retention --mode provider_data_share); Bedrock rejects
    // Fable 5 invocations with ValidationException under the default retention mode.
    [EnumMember(Value = "us.anthropic.claude-fable-5")]
    [Description("AWS Bedrock US Fable 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true)]
    UsAwsFable5 = 71,

    [EnumMember(Value = "anthropic.claude-opus-5")]
    [Description("AWS Bedrock Opus 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High)]
    AwsOpus5 = 75,

    [EnumMember(Value = "eu.anthropic.claude-opus-5")]
    [Description("AWS Bedrock EU Opus 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High)]
    EuAwsOpus5 = 76,

    [EnumMember(Value = "global.anthropic.claude-opus-5")]
    [Description("AWS Bedrock Global Opus 5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High)]
    GlobalAwsOpus5 = 77,

    [EnumMember(Value = "anthropic.claude-opus-5-5")]
    [Description("AWS Bedrock Opus 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true, defaultEffort: ModelEffort.Medium)]
    AwsOpus5_5 = 80,

    [EnumMember(Value = "eu.anthropic.claude-opus-5-5")]
    [Description("AWS Bedrock EU Opus 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true, defaultEffort: ModelEffort.Medium)]
    EuAwsOpus5_5 = 81,

    [EnumMember(Value = "global.anthropic.claude-opus-5-5")]
    [Description("AWS Bedrock Global Opus 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true, defaultEffort: ModelEffort.Medium)]
    GlobalAwsOpus5_5 = 82,

    [EnumMember(Value = "anthropic.claude-sonnet-5-5")]
    [Description("AWS Bedrock Sonnet 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High, isThinkingOffBetweenTools: true)]
    AwsSonnet5_5 = 85,

    [EnumMember(Value = "eu.anthropic.claude-sonnet-5-5")]
    [Description("AWS Bedrock EU Sonnet 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High, isThinkingOffBetweenTools: true)]
    EuAwsSonnet5_5 = 86,

    [EnumMember(Value = "global.anthropic.claude-sonnet-5-5")]
    [Description("AWS Bedrock Global Sonnet 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High, isThinkingOffBetweenTools: true)]
    GlobalAwsSonnet5_5 = 87,

    [EnumMember(Value = "anthropic.claude-haiku-5-5")]
    [Description("AWS Bedrock Haiku 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(defaultEffort: ModelEffort.Medium, maxEffortWithoutThinking: ModelEffort.High)]
    AwsHaiku5_5 = 90,

    [EnumMember(Value = "eu.anthropic.claude-haiku-5-5")]
    [Description("AWS Bedrock EU Haiku 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(defaultEffort: ModelEffort.Medium, maxEffortWithoutThinking: ModelEffort.High)]
    EuAwsHaiku5_5 = 91,

    [EnumMember(Value = "global.anthropic.claude-haiku-5-5")]
    [Description("AWS Bedrock Global Haiku 5.5")]
    [ActiveEnumEntry(false)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(defaultEffort: ModelEffort.Medium, maxEffortWithoutThinking: ModelEffort.High)]
    GlobalAwsHaiku5_5 = 92,

    #endregion

    #region Supported Models
    #region Haiku Series

    [EnumMember(Value = "us.anthropic.claude-haiku-4-5-20251001-v1:0")]
    [Description("AWS Bedrock US Haiku 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [VisualOrder(1005)]
    [MaxTokens(64000)]
    [ThinkingModel]
    UsAwsHaiku4_5 = 45,

    [EnumMember(Value = "us.anthropic.claude-haiku-5-5")]
    [Description("AWS Bedrock US Haiku 5.5")]
    [ActiveEnumEntry(false)]
    [VisualOrder(1006)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(defaultEffort: ModelEffort.Medium, maxEffortWithoutThinking: ModelEffort.High)]
    UsAwsHaiku5_5 = 93, // <================================================================ THIS IS THE LAST ENUMERATOR BY Oct 8, 2026

    #endregion

    #region Sonnet Series

    [EnumMember(Value = "us.anthropic.claude-sonnet-4-6")]
    [Description("AWS Bedrock US Sonnet 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry(false)]
    [VisualOrder(2023)]
    [MaxTokens(64000)]
    [ThinkingModel]
    UsAwsSonnet4_6 = 57,

    [EnumMember(Value = "us.anthropic.claude-sonnet-5")]
    [Description("AWS Bedrock US Sonnet 5")]
    [ActiveEnumEntry(false)]
    [VisualOrder(2024)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    UsAwsSonnet5 = 72,

    [EnumMember(Value = "us.anthropic.claude-sonnet-5-5")]
    [Description("AWS Bedrock US Sonnet 5.5")]
    [ActiveEnumEntry(false)]
    [VisualOrder(2025)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High, isThinkingOffBetweenTools: true)]
    UsAwsSonnet5_5 = 88,

    #endregion

    #region Opus Series

    [EnumMember(Value = "us.anthropic.claude-opus-4-8")]
    [Description("AWS Bedrock US Opus 4.8")]
    [ActiveEnumEntry(false)]
    [VisualOrder(3014)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    UsAwsOpus4_8 = 73,

    [EnumMember(Value = "us.anthropic.claude-opus-5")]
    [Description("AWS Bedrock US Opus 5")]
    [ActiveEnumEntry(false)]
    [VisualOrder(3015)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High)]
    UsAwsOpus5 = 78,

    [EnumMember(Value = "us.anthropic.claude-opus-5-5")]
    [Description("AWS Bedrock US Opus 5.5")]
    [ActiveEnumEntry(false)]
    [VisualOrder(3016)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true, defaultEffort: ModelEffort.Medium)]
    UsAwsOpus5_5 = 83,

    #endregion

    #region Model choices

    // One entry per model, offered in the AI Query model list. A block stores the plain name, and
    // ModelRouteResolver turns it into the id of the running stack's route. On Azure the plain name
    // is the Foundry deployment name only on a stack that names each deployment after its model,
    // as the Inia stacks do.

    [EnumMember(Value = "claude-haiku-4-5-20251001")]
    [Description("Claude Haiku 4.5")]
    [StandardVisionTier]
    [ActiveEnumEntry]
    [VisualOrder(1005)]
    [MaxTokens(64000)]
    [ThinkingModel]
    Haiku4_5 = 44,

    [EnumMember(Value = "claude-haiku-5-5")]
    [Description("Claude Haiku 5.5")]
    [ActiveEnumEntry]
    [VisualOrder(1006)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(defaultEffort: ModelEffort.Medium, maxEffortWithoutThinking: ModelEffort.High)]
    PublicHaiku5_5 = 89,

    [EnumMember(Value = "claude-sonnet-4-6")]
    [Description("Claude Sonnet 4.6")]
    [StandardVisionTier]
    [ActiveEnumEntry]
    [VisualOrder(2023)]
    [MaxTokens(64000)]
    [ThinkingModel]
    PublicSonnet4_6 = 50,

    [EnumMember(Value = "claude-sonnet-5")]
    [Description("Claude Sonnet 5")]
    [ActiveEnumEntry]
    [VisualOrder(2024)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    PublicSonnet5 = 63,

    [EnumMember(Value = "claude-sonnet-5-5")]
    [Description("Claude Sonnet 5.5")]
    [ActiveEnumEntry]
    [VisualOrder(2025)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High, isThinkingOffBetweenTools: true)]
    PublicSonnet5_5 = 84,

    [EnumMember(Value = "claude-opus-4-8")]
    [Description("Claude Opus 4.8")]
    [ActiveEnumEntry]
    [VisualOrder(3014)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel]
    PublicOpus4_8 = 59,

    [EnumMember(Value = "claude-opus-5")]
    [Description("Claude Opus 5")]
    [ActiveEnumEntry]
    [VisualOrder(3015)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(maxEffortWithoutThinking: ModelEffort.High)]
    PublicOpus5 = 74,

    [EnumMember(Value = "claude-opus-5-5")]
    [Description("Claude Opus 5.5")]
    [ActiveEnumEntry]
    [VisualOrder(3016)]
    [MaxTokens(128000)]
    [ThinkingModel]
    [AdaptiveThinkingModel(isThinkingAlwaysOn: true, defaultEffort: ModelEffort.Medium)]
    PublicOpus5_5 = 79,

    #endregion
    #endregion
}
