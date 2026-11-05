using System.Text;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class MapSearch
{
    /// <summary>
    /// Matches on whitespace-collapsed text, so a title carrying a stray double space still
    /// answers to the name a user reads off the screen and types back in.
    /// </summary>
    public static bool Matches(string? text, string? query)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        return CollapseWhitespace(text).Contains(CollapseWhitespace(query), StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool inWhitespace = false;
        foreach (char character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                inWhitespace = true;

                continue;
            }

            if (inWhitespace && builder.Length > 0)
            {
                _ = builder.Append(' ');
            }

            inWhitespace = false;
            _ = builder.Append(character);
        }

        return builder.ToString();
    }
}
