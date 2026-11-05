using IniaAi.WebApp.Theme;
using MudBlazor;
using MudBlazor.Utilities;

namespace IniaAi.WebApp.Tests.Theme;

public class IniaThemeContrastTests
{
    private const double WCAG_AA_BODY_TEXT = 4.5;
    private const double WCAG_ICONS_AND_LARGE_TEXT = 3.0;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryInkOnItsGround_MeetsItsWcagMinimum(bool isDarkMode)
    {
        Palette palette = IniaTheme.Palette(isDarkMode);
        // Product decision: light-mode status colours stay vivid, so they meet the icon and large-text minimum only.
        double statusMinimum = isDarkMode ? WCAG_AA_BODY_TEXT : WCAG_ICONS_AND_LARGE_TEXT;
        (string Pair, MudColor Ink, MudColor Ground, double Minimum)[] pairs =
        [
            ("text on background", palette.TextPrimary, palette.Background, WCAG_AA_BODY_TEXT),
            ("text on surface", palette.TextPrimary, palette.Surface, WCAG_AA_BODY_TEXT),
            ("secondary text on background", palette.TextSecondary, palette.Background, WCAG_AA_BODY_TEXT),
            ("secondary text on surface", palette.TextSecondary, palette.Surface, WCAG_AA_BODY_TEXT),
            ("app bar text", palette.AppbarText, palette.AppbarBackground, WCAG_AA_BODY_TEXT),
            ("drawer text", palette.DrawerText, palette.DrawerBackground, WCAG_AA_BODY_TEXT),
            ("primary on surface", palette.Primary, palette.Surface, WCAG_AA_BODY_TEXT),
            ("primary button label", palette.PrimaryContrastText, palette.Primary, WCAG_AA_BODY_TEXT),
            ("secondary on surface", palette.Secondary, palette.Surface, WCAG_AA_BODY_TEXT),
            ("secondary button label", palette.SecondaryContrastText, palette.Secondary, WCAG_AA_BODY_TEXT),
            ("error on surface", palette.Error, palette.Surface, statusMinimum),
            ("error on background", palette.Error, palette.Background, statusMinimum),
            ("error button label", palette.ErrorContrastText, palette.Error, statusMinimum),
            ("success on surface", palette.Success, palette.Surface, statusMinimum),
            ("success on background", palette.Success, palette.Background, statusMinimum),
            ("success button label", palette.SuccessContrastText, palette.Success, statusMinimum),
            ("warning on surface", palette.Warning, palette.Surface, statusMinimum),
            ("warning on background", palette.Warning, palette.Background, statusMinimum),
            ("warning button label", palette.WarningContrastText, palette.Warning, statusMinimum),
            ("info on surface", palette.Info, palette.Surface, WCAG_AA_BODY_TEXT),
            ("info on background", palette.Info, palette.Background, WCAG_AA_BODY_TEXT),
            ("info button label", palette.InfoContrastText, palette.Info, WCAG_AA_BODY_TEXT),
        ];

        List<string> failing = [.. pairs
            .Select(pair => (pair.Pair, pair.Minimum, Ratio: ContrastRatio(pair.Ink, pair.Ground)))
            .Where(pair => pair.Ratio < pair.Minimum)
            .Select(pair => $"{pair.Pair}: {pair.Ratio:F2}:1, needs {pair.Minimum}:1")];

        Assert.True(failing.Count == 0, $"Pairs below their minimum:\n{string.Join('\n', failing)}");
    }

    private static double ContrastRatio(MudColor first, MudColor second)
    {
        double lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        double darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(MudColor colour) =>
        (0.2126 * Linear(colour.R)) + (0.7152 * Linear(colour.G)) + (0.0722 * Linear(colour.B));

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
