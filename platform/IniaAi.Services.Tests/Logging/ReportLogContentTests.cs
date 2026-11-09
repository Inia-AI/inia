using System.Text;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces;
using IniaAi.Core.Workflows;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Tests.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Document = IniaAi.Domain.Entities.Documents.Document;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Services.Tests.Logging;

/// <summary>
/// A report read from its documents and run through the engine with a scripted model writes ids and
/// outcomes to the logs, never the document's text, the workflow's prompt or the model's reply.
/// Operators see a customer's content only with the customer's permission, and a log line would be
/// a way around that.
/// </summary>
public sealed class ReportLogContentTests : IClassFixture<DocumentsFixture>, IDisposable
{
    private const string DOCUMENT_MARKER = "QUOKKA-DOCUMENT-7F3A";
    private const string PROMPT_MARKER = "QUOKKA-PROMPT-9C1E";
    private const string REPLY_MARKER = "QUOKKA-REPLY-2B8D";

    private readonly DocumentsFixture _fixture;
    private readonly CapturingLoggerProvider _logs = new();
    private readonly ScriptedChatConnector _model = ScriptedChatConnector.Answering($"<answer>{REPLY_MARKER} reports net assets of 1,234,567.</answer>");
    private readonly IServiceProvider _services;
    private readonly IServiceScope _scope;

    public ReportLogContentTests(DocumentsFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _services = new TestServiceProviderBuilder(fixture.PlatformSettings, output).Build(services => services
            .AddSingleton<ILoggerProvider>(_logs)
            .AddSingleton<IChatConnector>(_model)
            .AddSingleton<IEmbeddingsConnector>(new WordHashEmbeddingsConnector()));
        _scope = _services.GetRequiredService<IServiceScopeFactory>().CreateScope();
    }

    [Fact]
    public async Task AReportReadFromItsDocuments_WritesNoDocumentTextPromptOrReplyToTheLogsAsync()
    {
        Workflow workflow = Report();
        List<DocumentInfo> documents = [Upload(TextDocument()), Upload(ScannedPage())];
        IUserDataUnitOfWork unitOfWork = _scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        var documentProcessing = _services.GetRequiredService<IDocumentProcessingService>();
        WorkflowHandler handler = _services.GetRequiredService<WorkflowHandler>();

        await documentProcessing.ProcessDocumentsAsync(documents, unitOfWork, workflow, cancellationToken: TestContext.Current.CancellationToken);
        await documentProcessing.LoadContextToWorkflowAsync(documents, workflow, handler.AllPages, cancellationToken: TestContext.Current.CancellationToken);
        _ = await handler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), unitOfWork, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(_model.Calls, call => call.Text.Contains(DOCUMENT_MARKER, StringComparison.Ordinal));
        Assert.Contains(_model.Calls, call => call.Text.Contains(PROMPT_MARKER, StringComparison.Ordinal));
        Assert.All(workflow.GetAllBlocks(), block => Assert.Contains(REPLY_MARKER, block.BlockResponse?.RawResponse, StringComparison.Ordinal));
        Assert.NotEmpty(_logs.Entries);
        string[] leaks = _logs.Entries.Holding(DOCUMENT_MARKER, PROMPT_MARKER, REPLY_MARKER);
        Assert.True(leaks.Length == 0, $"{leaks.Length} log lines carry customer content:{Environment.NewLine}{string.Join(Environment.NewLine, leaks)}");
    }

    [Fact]
    public async Task ACriticalBlockThatFailsOnTheRunsValues_KeepsTheReasonForTheReader_AndOutOfTheLogsAsync()
    {
        var workflow = new Workflow("Credit report");
        var section = new Section("Analysis", workflow);
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Reader",
            ReplacementTag = "reader",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarise the net assets.", SystemPromptTemplate = "You are a credit analyst." },
        };
        Block ratio = new(BlockTypes.CalculateNumber, section)
        {
            Name = "Ratio",
            ReplacementTag = "ratio",
            Properties = new CalculateFormulaProperties { FormulaTemplate = "addDays({reader}, 1)", IsCritical = true },
        };
        IUserDataUnitOfWork unitOfWork = _scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        WorkflowHandler handler = _services.GetRequiredService<WorkflowHandler>();

        CriticalBlockFailureException abort = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => handler.ExecuteWorkflowAsync(workflow, [], "testOrg", Guid.NewGuid().ToString(), unitOfWork, inputs: [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(REPLY_MARKER, ratio.ExecutionState?.Error, StringComparison.Ordinal);
        Assert.Contains(REPLY_MARKER, abort.FailureDetail, StringComparison.Ordinal);
        Assert.DoesNotContain(REPLY_MARKER, abort.ToString(), StringComparison.Ordinal);
        string[] leaks = _logs.Entries.Holding(REPLY_MARKER);
        Assert.True(leaks.Length == 0, $"{leaks.Length} log lines carry customer content:{Environment.NewLine}{string.Join(Environment.NewLine, leaks)}");
    }

    public void Dispose()
    {
        _scope.Dispose();
        (_services as IDisposable)?.Dispose();
    }

    private static Workflow Report()
    {
        var workflow = new Workflow("Credit report") { Configuration = new WorkflowConfiguration { PageOverviews = PageOverviewPromptDefaults.Create() } };
        var section = new Section("Analysis", workflow);
        var reader = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Reader",
            ReplacementTag = "reader",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"{PROMPT_MARKER}: summarise the net assets the documents report.",
                SystemPromptTemplate = "You are a credit analyst.",
                ShouldUsePageImages = false,
            },
        };
        _ = new RagSettings(reader) { Type = RagTypes.WholeDocument };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Opinion",
            ReplacementTag = "opinion",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"{PROMPT_MARKER}: give an opinion on this summary.\n{{reader}}",
                SystemPromptTemplate = "You are a credit analyst.",
            },
        };
        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Combined",
            ReplacementTag = "combined",
            Properties = new MergeProperties { TextTemplate = "{reader}\n{opinion}" },
        };
        _ = new Block(BlockTypes.SwitchCase, section)
        {
            Name = "Verdict",
            ReplacementTag = "verdict",
            Properties = new SwitchCaseProperties { InputTemplate = "{opinion}", Cases = new() { ["approve"] = "Approved" }, DefaultValue = $"{REPLY_MARKER} reviewed" },
        };
        return workflow;
    }

    private static Document TextDocument()
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"# Annual report\n\n{DOCUMENT_MARKER} holds net assets of 1,234,567 USD at the year end.\n");
        var file = new FileMetadata("IniaAi", "annual-report.txt", bytes);
        return new Document(file, MediaType.TextPlain) { FileMetadata = file };
    }

    private Document ScannedPage()
    {
        var file = new FileMetadata("IniaAi", "ManagerQs1.png", _fixture.ImageSample1.BinaryData.ToArray());
        return new Document(file, MediaType.ImagePng) { FileMetadata = file };
    }

    private static DocumentInfo Upload(Document document)
    {
        return new DocumentInfo(document.Id) { Document = document };
    }
}
