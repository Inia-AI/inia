using IniaAi.Common.Attributes;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;

namespace IniaAi.Common.Extensions;

public static class EnumExtensions
{
    public static string GetDescription(this Enum value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        FieldInfo? field = value.GetType().GetField(value.ToString());

        return field is null
            ? value.ToString()
            : Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attribute
                ? attribute.Description
                : value.ToString();
    }

    public static string GetEnumMember(this Enum value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        FieldInfo? field = value.GetType().GetField(value.ToString());

        return field is null
            ? value.ToString()
            : Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute)) is EnumMemberAttribute attribute
                ? attribute.Value
                    ?? value.ToString()
                : value.ToString();
    }

    public static bool IsActiveEnumEntry(this Enum value)
    {
        if (value is null)
        {
            return false;
        }

        FieldInfo? field = value.GetType().GetField(value.ToString());

        return field is not null
            && Attribute.GetCustomAttribute(field, typeof(ActiveEnumEntryAttribute)) is ActiveEnumEntryAttribute attribute
            && attribute.IsActive;
    }

    public static int GetVisualOrder(this Enum value)
    {
        if (value is null)
        {
            return -1;
        }

        FieldInfo? field = value.GetType().GetField(value.ToString());

        return field is not null
            && Attribute.GetCustomAttribute(field, typeof(VisualOrderAttribute)) is VisualOrderAttribute attribute
            ? attribute.Order
            : -1;
    }

    public static int? GetMaxTokens(this Enum value)
    {
        if (value is null)
        {
            return null;
        }

        FieldInfo? field = value.GetType().GetField(value.ToString());

        return field is not null
            && Attribute.GetCustomAttribute(field, typeof(MaxTokensAttribute)) is MaxTokensAttribute attribute
            ? attribute.MaxTokens
            : null;
    }

    /// <summary>
    /// The largest image the model reads without downscaling it.
    /// </summary>
    public static VisionTier GetVisionTier(this Enum value)
    {
        FieldInfo? field = value.GetType().GetField(value.ToString());

        return field is not null && Attribute.IsDefined(field, typeof(StandardVisionTierAttribute))
            ? VisionTier.Standard
            : VisionTier.HighResolution;
    }

    public static bool IsThinkingModel(this Enum value)
    {
        if (value is null)
        {
            return false;
        }

        FieldInfo? field = value.GetType().GetField(value.ToString());

        return field is not null
            && Attribute.GetCustomAttribute(field, typeof(ThinkingModelAttribute)) is ThinkingModelAttribute attribute
            && attribute.IsThinkingModel;
    }

    public static bool IsAdaptiveThinkingModel(this Enum value)
    {
        return value.GetAdaptiveThinkingModelAttribute()?.IsAdaptiveThinkingModel ?? false;
    }

    public static bool IsThinkingAlwaysOnModel(this Enum value)
    {
        return value.GetAdaptiveThinkingModelAttribute()?.IsThinkingAlwaysOn ?? false;
    }

    public static bool IsThinkingOffBetweenToolsModel(this Enum value)
    {
        return value.GetAdaptiveThinkingModelAttribute()?.IsThinkingOffBetweenTools ?? false;
    }

    public static ModelEffort GetDefaultEffort(this Enum value)
    {
        return value.GetAdaptiveThinkingModelAttribute()?.DefaultEffort ?? AdaptiveThinkingModelAttribute.DEFAULT_EFFORT;
    }

    /// <summary>
    /// The effort a request to the model carries: the chosen effort, lowered to the model's
    /// ceiling when the request switches thinking off on a model that refuses thinking-off
    /// requests above that ceiling.
    /// </summary>
    public static ModelEffort? GetEffortSent(this Enum value, bool shouldUseThinkingMode, ModelEffort? effort)
    {
        AdaptiveThinkingModelAttribute? attribute = value.GetAdaptiveThinkingModelAttribute();

        if (attribute is null || shouldUseThinkingMode || attribute.IsThinkingAlwaysOn)
        {
            return effort;
        }

        return effort > attribute.MaxEffortWithoutThinking ? attribute.MaxEffortWithoutThinking : effort;
    }

    private static AdaptiveThinkingModelAttribute? GetAdaptiveThinkingModelAttribute(this Enum value)
    {
        if (value is null)
        {
            return null;
        }

        FieldInfo? field = value.GetType().GetField(value.ToString());

        return field is null
            ? null
            : Attribute.GetCustomAttribute(field, typeof(AdaptiveThinkingModelAttribute)) as AdaptiveThinkingModelAttribute;
    }
}
