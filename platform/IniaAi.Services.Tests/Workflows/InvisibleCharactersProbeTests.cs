using IniaAi.Services.Tests.Documents;
using IniaAi.Testing.DependencyInjection.Mocks;

namespace IniaAi.Services.Tests.Workflows;

/// <summary>
/// Records, without a rule, what document processing and the model call do with characters a reader cannot see: a tag
/// character, a zero-width space and a variation selector between ordinary letters.
/// </summary>
public sealed class InvisibleCharactersProbeTests(DocumentsFixture fixture, ITestOutputHelper output) : IClassFixture<DocumentsFixture>
{
    private static readonly (string Name, string Character)[] _invisible =
    [
        ("U+E0041 tag letter A", char.ConvertFromUtf32(0xE0041)),
        ("U+200B zero-width space", "​"),
        ("U+FE0F variation selector 16", "️"),
    ];

    [Fact]
    public async Task ADocument_RecordsWhatHappensToInvisibleCharacters()
    {
        string text = $"Net{_invisible[0].Character}assets{_invisible[1].Character}of{_invisible[2].Character}1,234,567 USD at the year end.";
        using var run = new MarkerRun(fixture, output, ScriptedChatConnector.Answering("<answer>Net assets are 1,234,567.</answer>")) { DocumentText = text };

        _ = await run.RunAsync(TestContext.Current.CancellationToken);

        string readable = string.Concat(run.Document!.Pages.Select(page => page.ReadableText));
        string sent = string.Join('\n', run.Model.Calls.Select(call => call.Text));
        foreach ((string name, string character) in _invisible)
        {
            output.WriteLine($"{name}: in the page's readable text {readable.Contains(character, StringComparison.Ordinal)}; in a model call {sent.Contains(character, StringComparison.Ordinal)}");
        }

        Assert.NotEmpty(run.Model.Calls);
    }
}
