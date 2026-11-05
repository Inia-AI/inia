using System.Text.Json;

namespace IniaAi.Domain.Entities.Screening;

/// <summary>
/// A list stored in a screening JSON column. A column that is empty or does not parse reads as an
/// empty list, so one bad row never breaks a page that lists it.
/// </summary>
public static class ScreeningJsonList
{
    public static string Write<T>(IEnumerable<T> items)
    {
        return JsonSerializer.Serialize(items, JsonSerializerOptions.Web);
    }

    public static IReadOnlyList<T> Read<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonSerializerOptions.Web) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
