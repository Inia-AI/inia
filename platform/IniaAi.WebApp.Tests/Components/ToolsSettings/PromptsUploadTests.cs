using ErrorOr;
using IniaAi.Domain.Tools.PdfOcr;
using IniaAi.WebApp.Components.ToolsSettings.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Text;

namespace IniaAi.WebApp.Tests.Components.ToolsSettings;

public class PromptsUploadTests
{
    [Fact]
    public async Task AValidPromptsFile_ReadsAsThosePrompts()
    {
        PdfOcrPrompts edited = new() { TranscriptionPrompt = "transcribe every glyph" };
        InMemoryBrowserFile file = new("prompts.json", Encoding.UTF8.GetBytes(edited.ToJson(pretty: true)));

        ErrorOr<PdfOcrPrompts> result = await PromptsUpload.ReadAsync<PdfOcrPrompts>(file, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("transcribe every glyph", result.Value.TranscriptionPrompt);
    }

    [Fact]
    public async Task AFileOverTheLimit_IsRefusedBeforeItIsRead()
    {
        InMemoryBrowserFile file = new("huge.json", [], reportedSize: PromptsUpload.MAX_FILE_SIZE + 1);

        ErrorOr<PdfOcrPrompts> result = await PromptsUpload.ReadAsync<PdfOcrPrompts>(file, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("huge.json", result.FirstError.Description);
        Assert.Contains("too large", result.FirstError.Description);
        Assert.False(file.Opened);
    }

    [Fact]
    public async Task AFileThatIsNotPromptsJson_IsRefused()
    {
        InMemoryBrowserFile file = new("notes.json", Encoding.UTF8.GetBytes("not json at all"));

        ErrorOr<PdfOcrPrompts> result = await PromptsUpload.ReadAsync<PdfOcrPrompts>(file, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("Failed to parse prompts from JSON", result.FirstError.Description);
    }

    private sealed class InMemoryBrowserFile(string name, byte[] bytes, long? reportedSize = null) : IBrowserFile
    {
        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => reportedSize ?? bytes.Length;

        public string ContentType => "application/json";

        public bool Opened { get; private set; }

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            Opened = true;
            return new MemoryStream(bytes);
        }
    }
}
