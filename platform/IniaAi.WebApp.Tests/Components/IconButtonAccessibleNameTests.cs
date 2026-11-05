using IniaAi.Testing;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// An icon-only button shows no text, so a screen reader and a test that drives the app by name
/// know it only by its aria-label. Every MudIconButton in the web app carries one.
/// </summary>
public class IconButtonAccessibleNameTests
{
    private const string WEB_APP_DIRECTORY = "IniaAi.WebApp";
    private const string ICON_BUTTON_TAG = "<MudIconButton";
    private const string ARIA_LABEL = "aria-label=";

    [Fact]
    public void EveryIconButton_CarriesAnAccessibleName()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> unnamed = [.. Directory
            .EnumerateFiles(Path.Combine(repoRoot, WEB_APP_DIRECTORY), "*.razor", SearchOption.AllDirectories)
            .SelectMany(path => IconButtonTags(File.ReadAllText(path))
                .Where(tag => !tag.Contains(ARIA_LABEL, StringComparison.Ordinal))
                .Select(tag => $"{Path.GetRelativePath(repoRoot, path)}: {tag.Split('\n')[0].Trim()}"))];

        Assert.True(unnamed.Count == 0, $"Icon buttons without an aria-label:\n{string.Join('\n', unnamed)}");
    }

    [Fact]
    public void TheTagScan_ReadsPastALambdaToTheTagsOwnEnd()
    {
        const string MARKUP = """
            <MudIconButton OnClick="@(() => Go())" Icon="@Icons.Close"
                           aria-label="Close" />
            <MudIconButton OnClick="@(() => items.Where(i => i > 2))" />
            """;

        List<string> tags = [.. IconButtonTags(MARKUP)];

        Assert.Equal(2, tags.Count);
        Assert.Contains(ARIA_LABEL, tags[0], StringComparison.Ordinal);
        Assert.DoesNotContain(ARIA_LABEL, tags[1], StringComparison.Ordinal);
        Assert.EndsWith("/>", tags[1], StringComparison.Ordinal);
    }

    // A tag ends at the first '>' outside a quoted attribute value and outside parentheses, so the
    // '>' of a lambda or a comparison inside OnClick="@(...)" does not end it.
    private static IEnumerable<string> IconButtonTags(string markup)
    {
        int start = markup.IndexOf(ICON_BUTTON_TAG, StringComparison.Ordinal);
        while (start >= 0)
        {
            bool inQuotes = false;
            int depth = 0;
            int end = start + ICON_BUTTON_TAG.Length;
            for (; end < markup.Length; end++)
            {
                char current = markup[end];
                if (current == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && current == '(')
                {
                    depth++;
                }
                else if (!inQuotes && current == ')')
                {
                    depth--;
                }
                else if (!inQuotes && depth == 0 && current == '>')
                {
                    break;
                }
            }

            yield return markup[start..Math.Min(end + 1, markup.Length)];
            start = markup.IndexOf(ICON_BUTTON_TAG, end, StringComparison.Ordinal);
        }
    }
}
