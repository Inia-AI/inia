using MudBlazor;
using MudBlazor.Utilities;

namespace IniaAi.WebApp.Theme;

public static class IniaTheme
{
    /* The Vue app (its src/assets/main.css) holds the same brand tokens; change both
       together. Its muted status inks are replaced here by vivid ones, because the editor is a tool: in light
       mode they clear 3:1 (icons, chips, large text), not the 4.5:1 of small text. */
    public static readonly MudTheme MudTheme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1A3850",
            PrimaryContrastText = "#F6F2EC",
            Secondary = "#015294",
            SecondaryContrastText = "#FFFFFF",
            Tertiary = "#47657D",
            TertiaryContrastText = "#FFFFFF",
            Background = "#F6F2EC",
            BackgroundGray = "#EBE7E0",
            Surface = "#FFFFFF",
            AppbarBackground = "#1A3850",
            AppbarText = "#F6F2EC",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#47657D",
            DrawerIcon = "#47657D",
            TextPrimary = "#1A3850",
            TextSecondary = "#47657D",
            TextDisabled = "#758D9F",
            ActionDefault = "#47657D",
            ActionDisabled = "#A5B4C0",
            ActionDisabledBackground = "#EBE7E0",
            LinesDefault = "#A5B4C099",
            LinesInputs = "#A5B4C0",
            Divider = "#A5B4C0",
            TableLines = "#A5B4C099",
            Success = "#00A040",
            SuccessContrastText = "#FFFFFF",
            Warning = "#C77700",
            WarningContrastText = "#FFFFFF",
            Error = "#EF4444",
            ErrorContrastText = "#FFFFFF",
            Info = "#105993",
            InfoContrastText = "#FFFFFF",
            Black = "#000000",
        },

        PaletteDark = new PaletteDark
        {
            Primary = "#F6F2EC",
            PrimaryContrastText = "#1A3850",
            Secondary = "#C0D5EA",
            SecondaryContrastText = "#1A3850",
            Tertiary = "#B4C3CF",
            TertiaryContrastText = "#1A3850",
            Background = "#112434",
            BackgroundGray = "#244C6D",
            Surface = "#1A3850",
            AppbarBackground = "#F6F2EC",
            AppbarText = "#1A3850",
            DrawerBackground = "#1A3850",
            DrawerText = "#B4C3CF",
            DrawerIcon = "#B4C3CF",
            TextPrimary = "#F6F2EC",
            TextSecondary = "#B4C3CF",
            TextDisabled = "#889DAD",
            ActionDefault = "#B4C3CF",
            ActionDisabled = "#43627C",
            ActionDisabledBackground = "#244C6D",
            LinesDefault = "#43627C99",
            LinesInputs = "#43627C",
            Divider = "#43627C",
            TableLines = "#43627C99",
            Success = "#4ADE80",
            SuccessContrastText = "#1A3850",
            Warning = "#FBBF24",
            WarningContrastText = "#1A3850",
            Error = "#FF8A80",
            ErrorContrastText = "#1A3850",
            Info = "#C0D5EA",
            InfoContrastText = "#1A3850",
            Black = "#000000",
        },

        Typography = new Typography
        {
            Button = new ButtonTypography { TextTransform = "none" },
        },
    };

    public static readonly IReadOnlyDictionary<string, string> LoginCssVariables = LoginVariables(MudTheme.PaletteLight);
    public static readonly IReadOnlyDictionary<string, string> LoginDarkSchemeCssVariables = LoginVariables(MudTheme.PaletteDark);

    private static readonly IReadOnlyDictionary<string, string> _appBarOnLightPage = PaletteCssVariables(MudTheme.PaletteDark);
    private static readonly IReadOnlyDictionary<string, string> _appBarOnDarkPage = PaletteCssVariables(MudTheme.PaletteLight);

    public static readonly IReadOnlyDictionary<string, string> StripCardCssVariables = new Dictionary<string, string>
    {
        ["--inia-strip-card"] = Css(MudTheme.PaletteLight.Surface),
        ["--inia-strip-card-hover"] = Css(MudTheme.PaletteLight.BackgroundGray),
        ["--inia-strip-card-ink"] = Css(MudTheme.PaletteLight.TextPrimary),
        ["--inia-strip-card-border"] = Css(MudTheme.PaletteLight.LinesInputs),
    };

    public static Palette Palette(bool isDarkMode) => isDarkMode ? MudTheme.PaletteDark : MudTheme.PaletteLight;

    public static IReadOnlyDictionary<string, string> AppBarCssVariables(bool isDarkMode) =>
        isDarkMode ? _appBarOnDarkPage : _appBarOnLightPage;

    public static Dictionary<string, string> PaletteCssVariables(Palette palette)
    {
        Dictionary<string, string> variables = [];

        foreach ((string role, MudColor colour, MudColor text, string darken, string lighten) in new[]
        {
            ("primary", palette.Primary, palette.PrimaryContrastText, palette.PrimaryDarken, palette.PrimaryLighten),
            ("secondary", palette.Secondary, palette.SecondaryContrastText, palette.SecondaryDarken, palette.SecondaryLighten),
            ("tertiary", palette.Tertiary, palette.TertiaryContrastText, palette.TertiaryDarken, palette.TertiaryLighten),
            ("info", palette.Info, palette.InfoContrastText, palette.InfoDarken, palette.InfoLighten),
            ("success", palette.Success, palette.SuccessContrastText, palette.SuccessDarken, palette.SuccessLighten),
            ("warning", palette.Warning, palette.WarningContrastText, palette.WarningDarken, palette.WarningLighten),
            ("error", palette.Error, palette.ErrorContrastText, palette.ErrorDarken, palette.ErrorLighten),
            ("dark", palette.Dark, palette.DarkContrastText, palette.DarkDarken, palette.DarkLighten),
        })
        {
            variables[$"--mud-palette-{role}"] = Css(colour);
            variables[$"--mud-palette-{role}-rgb"] = Rgb(colour);
            variables[$"--mud-palette-{role}-text"] = Css(text);
            variables[$"--mud-palette-{role}-darken"] = darken;
            variables[$"--mud-palette-{role}-lighten"] = lighten;
            variables[$"--mud-palette-{role}-hover"] = Css(colour.SetAlpha(palette.HoverOpacity));
        }

        foreach ((string name, MudColor colour) in new[]
        {
            ("text-primary", palette.TextPrimary),
            ("text-secondary", palette.TextSecondary),
            ("text-disabled", palette.TextDisabled),
            ("surface", palette.Surface),
            ("divider", palette.Divider),
        })
        {
            variables[$"--mud-palette-{name}"] = Css(colour);
            variables[$"--mud-palette-{name}-rgb"] = Rgb(colour);
        }

        foreach ((string name, MudColor colour) in new[]
        {
            ("action-default", palette.ActionDefault),
            ("action-disabled", palette.ActionDisabled),
            ("action-disabled-background", palette.ActionDisabledBackground),
            ("background", palette.Background),
            ("background-gray", palette.BackgroundGray),
            ("lines-default", palette.LinesDefault),
            ("lines-inputs", palette.LinesInputs),
            ("table-lines", palette.TableLines),
            ("white", palette.White),
            ("black", palette.Black),
        })
        {
            variables[$"--mud-palette-{name}"] = Css(colour);
        }

        variables["--mud-palette-action-default-hover"] = Css(palette.ActionDefault.SetAlpha(palette.HoverOpacity));

        return variables;
    }

    private static Dictionary<string, string> LoginVariables(Palette palette)
    {
        Dictionary<string, string> variables = PaletteCssVariables(palette);
        variables["--mud-elevation-8"] = MudTheme.Shadows.Elevation[8];
        variables["--google-button-fill"] = "#FFFFFF";
        variables["--google-button-fill-hover"] = "#F7F8F8";
        variables["--google-button-stroke"] = "#DADCE0";
        variables["--google-button-ink"] = "#1F1F1F";
        return variables;
    }

    private static string Css(MudColor colour) => colour.ToString(MudColorOutputFormats.RGBA);

    private static string Rgb(MudColor colour) => colour.ToString(MudColorOutputFormats.ColorElements);
}
