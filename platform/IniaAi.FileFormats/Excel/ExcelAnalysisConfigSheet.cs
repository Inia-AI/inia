using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.FileFormats.Excel;

// Single source of truth for the ExcelAnalysisConfig on-disk layout.
//
// Layout (very-hidden sheet "__IniaAiAnalysisConfig__"):
//   A1 = "SchemaVersion"   B1 = <int>          (readable even if JSON shape changes)
//   A2 = "ConfigJson"      B2 = <json payload> (camelCase, string-enum values)
public static class ExcelAnalysisConfigSheet
{
    public const string SHEET_NAME = "__IniaAiAnalysisConfig__";
    public const int CURRENT_SCHEMA_VERSION = 1;

    public const string KEY_SCHEMA_VERSION = "SchemaVersion";
    public const string KEY_CONFIG_JSON = "ConfigJson";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false,
    };

    public static string SerializePayload(ExcelAnalysisConfig config)
    {
        return JsonSerializer.Serialize(new ExcelAnalysisConfigPayload(config.Source), _jsonOptions);
    }

    /// <summary>Writes <paramref name="config"/> into the very hidden sheet that <see cref="TryRead"/> reads, replacing an earlier one.</summary>
    public static void Stamp(IEditableWorkbook workbook, ExcelAnalysisConfig config)
    {
        IEditableSheet sheet = workbook.ReplaceSheet(SHEET_NAME, SheetVisibility.VeryHidden);
        sheet.WriteText(0, 0, KEY_SCHEMA_VERSION, []);
        sheet.WriteNumber(0, 1, config.SchemaVersion);
        sheet.WriteText(1, 0, KEY_CONFIG_JSON, []);
        sheet.WriteText(1, 1, SerializePayload(config), []);
    }

    /// <summary>The workbook in <paramref name="workbookBytes"/> with <paramref name="config"/> stamped into it.</summary>
    public static byte[] Stamp(IEditableWorkbookFormat format, byte[] workbookBytes, ExcelAnalysisConfig config)
    {
        using IEditableWorkbook workbook = format.Open(workbookBytes);
        Stamp(workbook, config);
        return workbook.Save();
    }

    public static ExcelAnalysisConfig? TryRead(IWorkbookDocument workbook)
    {
        IWorkbookSheet? sheet = workbook.Sheets.FirstOrDefault(
            candidate => string.Equals(candidate.Name, SHEET_NAME, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            return null;
        }

        if (!int.TryParse(sheet.CellText(0, 1), out int schemaVersion))
        {
            return null;
        }

        if (schemaVersion != CURRENT_SCHEMA_VERSION)
        {
            return null;
        }

        string json = sheet.CellText(1, 1);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            ExcelAnalysisConfigPayload? payload =
                JsonSerializer.Deserialize<ExcelAnalysisConfigPayload>(json, _jsonOptions);
            return payload is null
                ? null
                : new ExcelAnalysisConfig(schemaVersion, payload.Source);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ExcelAnalysisConfigPayload(ExcelAnalysisSource Source);
}
