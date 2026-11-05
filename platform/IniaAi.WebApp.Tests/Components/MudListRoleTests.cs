using System.Text.RegularExpressions;
using IniaAi.Testing;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// MudBlazor 9.12 announces every MudList that is not ReadOnly as a listbox of selectable options.
/// A list the user does not pick from, such as a version history with buttons on each row, has to
/// say ReadOnly, or a screen reader hears its rows as options with the buttons folded into them.
/// </summary>
public partial class MudListRoleTests
{
    private const string WEB_APP_DIRECTORY = "IniaAi.WebApp";

    [GeneratedRegex(@"<MudList\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex MudListTag();

    [Fact]
    public void EveryMudList_IsReadOnly_OrBindsTheSelectionItOffers()
    {
        string webApp = Path.Combine(RepositoryRoot.Find(), WEB_APP_DIRECTORY);

        List<string> listboxes = [.. Directory
            .EnumerateFiles(webApp, "*.razor", SearchOption.AllDirectories)
            .SelectMany(path => MudListTag().Matches(File.ReadAllText(path))
                .Where(tag => !IsReadOnlyOrSelecting(tag.Value))
                .Select(tag => $"{Path.GetRelativePath(webApp, path)}: {tag.Value.Split('\n')[0].Trim()}"))];

        Assert.True(listboxes.Count == 0, $"MudLists announced as listboxes with no selection:\n{string.Join('\n', listboxes)}");
    }

    private static bool IsReadOnlyOrSelecting(string tag) =>
        tag.Contains("ReadOnly=\"true\"", StringComparison.Ordinal)
        || tag.Contains("SelectedValue", StringComparison.Ordinal);
}
