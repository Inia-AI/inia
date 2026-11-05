using IniaAi.Testing;
using System.Text.RegularExpressions;

namespace IniaAi.WebApp.Tests.Components.Documents;

/// <summary>
/// The upload card lets its host vouch for what is uploaded, which narrows the intake gate to the
/// refusals that protect the platform. Only a page that hosts the workflow editor may do that, and
/// only for a user the editor trusts: a page outside the editor that hosts the card must not vouch,
/// and the editor pages admit roles a customer may hold. A closed module's designer page is an
/// editor page wherever its folder sits.
/// </summary>
public partial class DocumentUploadCardTrustTests
{
    private const string WEB_APP_DIRECTORY = "IniaAi.WebApp";
    private const string CARD_TAG = "<DocumentUploadCard";
    private const string VOUCHED_ATTRIBUTE = "ContentIsVouchedFor";
    private const string VOUCHED_FOR_TRUSTED_EDITORS = "ContentIsVouchedFor=\"AppState.IsTrustedEditor\"";

    [GeneratedRegex(@"<WorkflowEditor\b")]
    private static partial Regex EditorTag();

    [Fact]
    public void OnlyTheEditorPages_VouchForWhatIsUploaded_AndOnlyForATrustedEditor()
    {
        string repoRoot = RepositoryRoot.Find();

        List<(string Path, bool IsEditorPage, bool Vouches, bool AsksTheEditor)> hosts = [.. Directory
            .EnumerateFiles(Path.Combine(repoRoot, WEB_APP_DIRECTORY), "*.razor", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), Text: File.ReadAllText(path)))
            .Where(host => host.Text.Contains(CARD_TAG, StringComparison.Ordinal) && !host.Path.EndsWith("/DocumentUploadCard.razor", StringComparison.Ordinal))
            .Select(host => (
                host.Path,
                IsEditorPage: EditorTag().IsMatch(host.Text),
                Vouches: host.Text.Contains(VOUCHED_ATTRIBUTE, StringComparison.Ordinal),
                AsksTheEditor: host.Text.Contains(VOUCHED_FOR_TRUSTED_EDITORS, StringComparison.Ordinal)))];

        Assert.NotEmpty(hosts);
        Assert.All(hosts, host => Assert.True(
            host.Vouches == host.IsEditorPage,
            $"{host.Path} {(host.Vouches ? "vouches for its uploads outside the editor" : "is an editor page that does not vouch for its uploads")}."));
        Assert.Contains(hosts, host => host.IsEditorPage);
        Assert.All(hosts.Where(host => host.IsEditorPage), page => Assert.True(
            page.AsksTheEditor,
            $"{page.Path} vouches for its uploads without asking whether the editor trusts the user."));
    }
}
