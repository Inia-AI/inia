using IniaAi.Configuration.Platform;
using IniaAi.WebApp.Theme;
using Microsoft.Extensions.Hosting;

namespace IniaAi.WebApp.Services;

public sealed record EnvironmentIndicator(bool Show, string Label, string Color, string TextColor);

public static class EnvironmentIndicatorResolver
{
    public const string LABEL_LOCAL = "LOCAL";

    private const string ENVIRONMENT_LOCAL_DEVELOPMENT = "LocalDevelopment";
    private const int BRIGHTNESS_THRESHOLD = 128;

    public static EnvironmentIndicator Resolve(string environmentName, EnvironmentBannerSettings banner)
    {
        bool hasCustomContent = !string.IsNullOrWhiteSpace(banner.Label)
            || !string.IsNullOrWhiteSpace(banner.Color);

        bool show = banner.Enabled ?? (!IsProduction(environmentName) || hasCustomContent);

        string label = string.IsNullOrWhiteSpace(banner.Label)
            ? ResolveDefaultLabel(environmentName)
            : banner.Label;

        string color = IsValidHexColor(banner.Color)
            ? banner.Color!
            : ResolveDefaultColor(environmentName);

        return new EnvironmentIndicator(show, label, color, ResolveTextColor(color));
    }

    private static bool IsProduction(string environmentName)
    {
        return string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDefaultLabel(string environmentName)
    {
        if (string.Equals(environmentName, ENVIRONMENT_LOCAL_DEVELOPMENT, StringComparison.OrdinalIgnoreCase))
        {
            return LABEL_LOCAL;
        }

        return environmentName.ToUpperInvariant();
    }

    private static string ResolveDefaultColor(string environmentName)
    {
        if (IsProduction(environmentName))
        {
            return EnvironmentColours.PRODUCTION;
        }

        if (string.Equals(environmentName, Environments.Staging, StringComparison.OrdinalIgnoreCase))
        {
            return EnvironmentColours.STAGING;
        }

        if (string.Equals(environmentName, ENVIRONMENT_LOCAL_DEVELOPMENT, StringComparison.OrdinalIgnoreCase))
        {
            return EnvironmentColours.LOCAL_DEVELOPMENT;
        }

        return EnvironmentColours.NON_PRODUCTION_DEFAULT;
    }

    private static bool IsValidHexColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color) || color[0] != '#')
        {
            return false;
        }

        if (color.Length != 4 && color.Length != 7)
        {
            return false;
        }

        for (int i = 1; i < color.Length; i++)
        {
            if (!Uri.IsHexDigit(color[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveTextColor(string hexColor)
    {
        (int red, int green, int blue) = ParseHexColor(hexColor);
        int brightness = ((red * 299) + (green * 587) + (blue * 114)) / 1000;
        return brightness > BRIGHTNESS_THRESHOLD ? EnvironmentColours.TEXT_DARK : EnvironmentColours.TEXT_LIGHT;
    }

    private static (int Red, int Green, int Blue) ParseHexColor(string hexColor)
    {
        string hex = hexColor.Substring(1);
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        int red = Convert.ToInt32(hex.Substring(0, 2), 16);
        int green = Convert.ToInt32(hex.Substring(2, 2), 16);
        int blue = Convert.ToInt32(hex.Substring(4, 2), 16);
        return (red, green, blue);
    }
}
