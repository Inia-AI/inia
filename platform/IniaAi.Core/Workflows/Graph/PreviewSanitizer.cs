namespace IniaAi.Core.Workflows.Graph;

public static class PreviewSanitizer
{
    public static (string? preview, int fullLength) Sanitize(string? input, int maxChars = 3000)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (null, 0);
        }

        int fullLength = input.Length;
        int previewLength = Math.Min(fullLength, maxChars);
        string preview = input[..previewLength];

        return (preview, fullLength);
    }
}
