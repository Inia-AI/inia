using IniaAi.Common.Enums;
using IniaAi.Core.Services;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Testing.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;

namespace IniaAi.Core.Tests.Services;

/// <summary>
/// A spreadsheet's pages are summarised one at a time, unless a module's workflow type asks for
/// each page to be read with its neighbours, as a PDF page is.
/// </summary>
public class DocumentAnalysisSpreadsheetContextTests : IClassFixture<DocumentsFixture>
{
    private const string PAGES_SYSTEM_PROMPT = "Summarise the page with its neighbours.";
    private const string SINGLE_PAGE_SYSTEM_PROMPT = "Summarise the page on its own.";
    private const string CONTEXT_READING_TYPE = "SheetReader";

    private static readonly ModuleWorkflowType _contextReadingType = new(CONTEXT_READING_TYPE, "Sheet Reader", () => new Workflow("sheet-reader"))
    {
        SpreadsheetOverviewsReadNeighbouringPages = true,
    };

    private readonly DocumentsFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DocumentAnalysisSpreadsheetContextTests(DocumentsFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ASpreadsheetOfAnOrdinaryType_IsSummarisedPageByPage()
    {
        ScriptedChatConnector chat = await SummariseSpreadsheetAsync("Corp");

        Assert.All(chat.Calls, call => Assert.Equal(SINGLE_PAGE_SYSTEM_PROMPT, call.SystemPrompt));
    }

    [Fact]
    public async Task ASpreadsheetOfATypeThatAsksForContext_IsSummarisedWithItsNeighbours()
    {
        ScriptedChatConnector chat = await SummariseSpreadsheetAsync(CONTEXT_READING_TYPE);

        Assert.All(chat.Calls, call => Assert.Equal(PAGES_SYSTEM_PROMPT, call.SystemPrompt));
    }

    private async Task<ScriptedChatConnector> SummariseSpreadsheetAsync(string workflowTypeId)
    {
        ScriptedChatConnector chat = ScriptedChatConnector.Answering("<answer>Page 1: a sheet</answer>");
        var service = new DocumentAnalysisService(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<DocumentAnalysisService>(_output),
            chat,
            _fixture.GetTenantModelFamilyResolver(_output),
            new ServiceScopeFactory(),
            DocumentsFixture.GetFileStorageManager(),
            FileFormatsUnderTest.Pdf,
            new ModuleWorkflowTypes([_contextReadingType]));
        var document = new Document(
            new FileMetadata("org-1", "book.xlsx", [0]),
            MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet);
        _ = new Page(document, 1, 1) { Text = "Sheet one" };
        _ = new Page(document, 2, 2) { Text = "Sheet two" };
        var prompts = new PageOverviewPrompts
        {
            Pages = new PageOverviewPrompt { SystemPrompt = PAGES_SYSTEM_PROMPT, UserPrompt = "{0}" },
            SinglePage = new PageOverviewPrompt { SystemPrompt = SINGLE_PAGE_SYSTEM_PROMPT, UserPrompt = "{0}" },
        };

        await service.GenerateOverviewsAsync(document, "org-1", classPrompts: null, prompts, workflowTypeId, TestContext.Current.CancellationToken);

        Assert.NotEmpty(chat.Calls);
        return chat;
    }
}
