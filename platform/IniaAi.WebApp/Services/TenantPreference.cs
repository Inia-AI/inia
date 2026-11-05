namespace IniaAi.WebApp.Services;

public static class TenantPreference
{
    public const string STORAGE_KEY = "inia.organization";

    public static bool ShouldRestore(string? storedValue, string? currentValue, IEnumerable<string> accessibleIds)
    {
        if (string.IsNullOrEmpty(storedValue) || storedValue == currentValue)
        {
            return false;
        }

        return accessibleIds.Contains(storedValue);
    }
}
