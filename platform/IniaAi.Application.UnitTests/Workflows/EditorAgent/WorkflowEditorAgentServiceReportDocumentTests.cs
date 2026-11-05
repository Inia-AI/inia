using System.Text;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Proposals;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

/// <summary>
/// A staged report's documents: the run's inputs and outputs as originals and as their stored
/// page text, within the message's limits, and only the organization's own.
/// </summary>
public sealed partial class WorkflowEditorAgentServiceTests
{
    private const string OUTPUT_CLASS = "SpreadOutput";
    private const string INPUT_NAME = "acme-annual-2025.pdf";
    private const string OUTPUT_NAME = "Acme spread.xlsx";
    private const string FOREIGN_NAME = "theirs-secret.pdf";
    private const string PLAIN_NOTES = "Analyst notes: covenant headroom is thin this year.";
    private const string LEASE_PAGE = "Note 14. Right-of-use assets comprise leased offices and drilling rigs, depreciated straight-line.";

    [Fact]
    public async Task PickedReport_StagesItsInputAndOutputDocumentsWithPageText_AndNoOtherOrganizationsDocument()
    {
        string picked = await SeedReportAsync(ORG_ID, EditorAgentWorkflows.TYPE, ACME, WorkflowState.Draft, daysAgo: 1);
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.7 the annual report");
        byte[] xlsx = [0x50, 0x4B, 0x03, 0x04, 0x01];
        await SeedRunDocumentAsync(picked, ORG_ID, INPUT_NAME, EditorAgentWorkflows.DOCUMENT_CLASS, MediaType.ApplicationPdf, pdf, "Cover page", LEASE_PAGE);
        await SeedRunDocumentAsync(picked, ORG_ID, OUTPUT_NAME, OUTPUT_CLASS, MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet, xlsx, "Balance sheet: lease liabilities 412");
        await SeedRunDocumentAsync(picked, OTHER_ORG_ID, FOREIGN_NAME, EditorAgentWorkflows.DOCUMENT_CLASS, MediaType.ApplicationPdf, pdf, "Their page");
        EditorAgentConversationView conversation = await StartAsync();

        EditorAgentTurnView turn = (await Service.SendMessageAsync(
            new EditorAgentMessage(ORG_ID, USER_ID, conversation.Id, "How are leases read?", SONNET, EditorAgentWorkflows.Build())
            {
                ReportIds = [picked],
            },
            TestContext.Current.CancellationToken)).Value;
        await WaitForSettledAsync(conversation.Id, turn.Number);

        AgentRunStartRequest run = Assert.Single(_client.StartRequests);
        string prefix = $"report-{picked}";
        AgentRunDocument input = Assert.Single(run.Documents, document => document.Name == $"{prefix}.in.{INPUT_NAME}");
        Assert.Equal(pdf, input.Content.ToArray());
        Assert.Equal("application/pdf", input.MediaType);
        AgentRunDocument output = Assert.Single(run.Documents, document => document.Name == $"{prefix}.out.{OUTPUT_NAME}");
        Assert.Equal(xlsx, output.Content.ToArray());

        JArray manifest = (JArray)JObject.Parse(DocumentText(run, EditorAgentReportFiles.ManifestName(picked)))["documents"]!;
        Assert.Equal(["input", "output"], manifest.Select(static document => (string?)document["role"]));
        Assert.Equal(2, (int)manifest[0]["pages"]!);
        string marker = (string)manifest[0]["pageMarker"]!;
        Assert.Matches("^[0-9a-f]{12}$", marker);

        string pages = DocumentText(run, $"{prefix}.in.{INPUT_NAME}.pages.txt");
        Assert.Contains($"=== {marker} page 2 ===\n{LEASE_PAGE}", pages.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("lease liabilities 412", DocumentText(run, $"{prefix}.out.{OUTPUT_NAME}.pages.txt"), StringComparison.Ordinal);

        string header = DocumentText(run, EditorAgentReports.HeaderDocumentName(picked));
        Assert.Contains("## Documents of the run", header, StringComparison.Ordinal);
        Assert.Contains($"`{prefix}.in.{INPUT_NAME}`", header, StringComparison.Ordinal);
        Assert.Contains($"`{prefix}.out.{OUTPUT_NAME}.pages.txt`", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Left out", header, StringComparison.Ordinal);

        Assert.DoesNotContain(run.Documents, document => document.Name.Contains("theirs", StringComparison.Ordinal));
        Assert.All(run.Documents, document => Assert.DoesNotContain("Their page", Encoding.UTF8.GetString(document.Content.Span), StringComparison.Ordinal));
        Assert.DoesNotContain(LEASE_PAGE, run.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportDocumentsPastTheLimits_AreListedAsLeftOut_AndTheirPageTextCountsAsReportText()
    {
        var summary = new EditorAgentReportSummary("run-1", DateTime.UtcNow, ACME, "completed", "Report", [INPUT_NAME]);
        EditorAgentReportEvidence evidence = EditorAgentReports.Evidence(summary, failure: null, Executed("run-1", ORG_ID, EditorAgentWorkflows.TYPE), long.MaxValue);
        const long MEGABYTE = EditorAgentReportFiles.BYTES_PER_MEGABYTE;
        EditorAgentRunDocument[] documents =
        [
            RunDocument("small.pdf", isOutput: false, MEGABYTE, LEASE_PAGE),
            RunDocument("huge-scan.pdf", isOutput: false, 60 * MEGABYTE, "Scanned page"),
            RunDocument("second.pdf", isOutput: false, 3 * MEGABYTE, "Another page"),
            RunDocument("spread.xlsx", isOutput: true, MEGABYTE),
            RunDocument("notes.txt", isOutput: false, 64) with { MediaType = "text/plain" },
        ];
        var budget = new EditorAgentReportBudget(4 * MEGABYTE, files: 20);
        var read = new List<string>();

        EditorAgentReportFiles files = await EditorAgentReportFiles.BuildAsync(
            "run-1", documents, budget, maxFileBytes: 50 * MEGABYTE,
            (document, _) =>
            {
                read.Add(document.Name);
                return Task.FromResult<byte[]?>(document.MediaType == "text/plain"
                    ? Encoding.UTF8.GetBytes(PLAIN_NOTES)
                    : new byte[document.SizeBytes]);
            },
            TestContext.Current.CancellationToken);
        EditorAgentReportEvidence staged = files.AddTo(evidence);

        Assert.Equal(["small.pdf", "notes.txt"], read);
        string header = Encoding.UTF8.GetString(staged.Documents[0].Content.Span);
        Assert.Contains("Left out:", header, StringComparison.Ordinal);
        Assert.Contains("- huge-scan.pdf (input original, 60.0 MB): the original is over the 50.0 MB per-file limit.", header, StringComparison.Ordinal);
        Assert.Contains("- second.pdf (input original, 3.0 MB): the message's limit for report documents is used up.", header, StringComparison.Ordinal);
        Assert.Contains("spread.xlsx (SpreadOutput): 0 pages, 1.0 MB; original not staged; no stored text", header, StringComparison.Ordinal);
        Assert.Contains($"- spread.xlsx (output original, 1.0 MB): {EditorAgentReportFiles.NO_TEXT_TO_CHECK}.", header, StringComparison.Ordinal);
        Assert.DoesNotContain(staged.Documents, document => document.Name == "report-run-1.out.spread.xlsx");
        Assert.Contains(staged.Documents, document => document.Name == "report-run-1.in.notes.txt");
        Assert.Contains(PLAIN_NOTES, staged.RunTexts);
        Assert.Contains(staged.Documents, document => document.Name == "report-run-1.in.huge-scan.pdf.pages.txt");
        Assert.DoesNotContain(staged.Documents, document => document.Name == "report-run-1.in.huge-scan.pdf");
        Assert.True(budget.BytesLeft >= 0);

        var leaks = new EditorAgentReportLeaks([staged], []);
        ProposedChange quoted = new()
        {
            Id = Guid.NewGuid().ToString(),
            Scope = ProposedChangeScope.Block,
            Kind = ProposedChangeKind.FieldEdited,
            TargetKey = "stable-producer",
            Field = "Properties.UserPromptTemplate",
            BaseJson = Newtonsoft.Json.JsonConvert.SerializeObject("Read the lease note."),
            NewJson = Newtonsoft.Json.JsonConvert.SerializeObject("Read the lease note, for example: " + LEASE_PAGE),
        };
        Assert.Single(leaks.Find([quoted], []));
    }

    private static EditorAgentRunDocument RunDocument(string name, bool isOutput, long sizeBytes, params string[] pages)
    {
        return new EditorAgentRunDocument(
            Guid.NewGuid().ToString(),
            name,
            isOutput ? OUTPUT_CLASS : EditorAgentWorkflows.DOCUMENT_CLASS,
            isOutput,
            "application/octet-stream",
            sizeBytes,
            [.. pages.Select(static (text, index) => new EditorAgentRunPage(index + 1, index + 1, PageLocation.FullPage, text))]);
    }

    /// <summary>
    /// A document filed on the run's job with its stored bytes and pages. A document of
    /// <see cref="OUTPUT_CLASS"/> is an output: the executed workflow marks that class so.
    /// </summary>
    private async Task SeedRunDocumentAsync(
        string executionId,
        string organizationId,
        string name,
        string documentClass,
        MediaType mediaType,
        byte[] bytes,
        params string[] pages)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        string jobId = await context.WorkflowExecutions
            .Where(execution => execution.Id == executionId)
            .Select(static execution => execution.JobId!)
            .SingleAsync(TestContext.Current.CancellationToken);

        if (documentClass == OUTPUT_CLASS)
        {
            Workflow executed = await context.Workflows
                .Include(static workflow => workflow.DocumentClasses)
                .SingleAsync(workflow => workflow.Id == executionId, TestContext.Current.CancellationToken);
            _ = new DocumentClass(executed, OUTPUT_CLASS) { IsOutputDocument = true };
        }

        string storagePath = Guid.NewGuid().ToString();
        _storage.AddTestFile(organizationId, StorageCategories.Documents, storagePath, bytes);
        var file = new FileMetadata(organizationId, name, bytes.Length, StorageProvider.Database, storagePath);
        var document = new Document(file, mediaType) { OrganizationId = organizationId, Name = Path.GetFileNameWithoutExtension(name) };
        for (int index = 0; index < pages.Length; index++)
        {
            _ = new Page(document, index + 1, index + 1) { Text = pages[index], Status = PageStatus.ProcessedSuccessfully };
        }

        _ = context.FileMetadata.Add(file);
        _ = context.Documents.Add(document);
        _ = context.DocumentInfos.Add(new DocumentInfo(document.Id) { JobId = jobId, DocumentClass = documentClass });
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
