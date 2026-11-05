namespace IniaAi.WebApp.Services;

public static class ThemePreference
{
    public const string STORAGE_KEY = "inia.theme";
    public const string LIGHT = "light";
    public const string DARK = "dark";

    public static bool IsDarkMode(string? storedValue)
    {
        return storedValue != LIGHT;
    }

    public static string FromIsDarkMode(bool isDarkMode)
    {
        return isDarkMode ? DARK : LIGHT;
    }
}
