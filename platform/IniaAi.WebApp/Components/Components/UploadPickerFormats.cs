using IniaAi.Common;
using IniaAi.FileFormats.Inspection;

namespace IniaAi.WebApp.Components.Components;

/// <summary>The file types the upload card's picker offers: every type the card takes that this build reads.</summary>
public static class UploadPickerFormats
{
    private static readonly string[] _extensions = [".pdf", ".jpg", ".jpeg", ".png", ".csv", ".xls", ".xlsx", ".xlsm", ".docx", ".msg", ".eml"];

    public static string Accept(IReadableFormats formats)
    {
        return string.Join(", ", _extensions.Where(extension => formats.Refusal(Utils.DetermineMediaTypeFromExtension(extension)) is null));
    }
}
