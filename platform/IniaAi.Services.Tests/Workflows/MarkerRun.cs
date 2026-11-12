using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using IniaAi.Services.Export.Models;
using IniaAi.Services.Export;
using IniaAi.FileFormats.Markdown;
using IniaAi.Domain.Enums;
using IniaAi.Configuration;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Application.Common.Services;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Tests.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Document = IniaAi.Domain.Entities.Documents.Document;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Services.Tests.Workflows;

/// <summary>
/// One run of <see cref="MarkerWorkflow"/> over a text document, through document processing and the
/// engine, with a scripted model, writing its execution and report rows as a job does. A test may add
/// blocks to <see cref="Workflow"/> before it runs, and then searches what a reader gets from the run
/// for <see cref="MarkerWorkflow.Instructions"/>.
/// </summary>
public sealed class MarkerRun : IDisposable
{
    public const string ORGANIZATION_ID = "testOrg";
    public const string DOCUMENT_TEXT = "The company holds net assets of 1,234,567 USD at the year end.";

    private readonly IServiceProvider _services;
    private readonly IServiceScope _scope;

    public MarkerRun(DocumentsFixture fixture, ITestOutputHelper output, IChatConnector model, Action<IServiceCollection>? configure = null)
    {
        Connector = model;
        _services = new TestServiceProviderBuilder(fixture.PlatformSettings, output).Build(services =>
        {
            _ = services
                .AddSingleton<ILoggerProvider>(Logs)
                .AddSingleton(Connector)
                .AddSingleton<IEmbeddingsConnector>(new WordHashEmbeddingsConnector());
            configure?.Invoke(services);
        });
        _scope = _services.GetRequiredService<IServiceScopeFactory>().CreateScope();
    }

    public Workflow Workflow { get; init; } = MarkerWorkflow.Build();

    /// <summary>The text of the run's one document; set it before <see cref="RunAsync"/>.</summary>
    public string DocumentText { get; init; } = DOCUMENT_TEXT;

    /// <summary>The run's documents in place of the one text document of <see cref="DocumentText"/>; set them before <see cref="RunAsync"/>.</summary>
    public IReadOnlyList<MarkerFile>? Files { get; init; }

    /// <summary>The run's first document, with its pages once <see cref="RunAsync"/> has processed it.</summary>
    public Document? Document { get; private set; }

    public string JobId { get; } = Guid.NewGuid().ToString();

    /// <summary>The run's execution, which is also its report's id; set once <see cref="RunAsync"/> has started the run.</summary>
    public string? ExecutionId { get; private set; }

    public IChatConnector Connector { get; }

    /// <summary>The scripted model the run answers from, with every call it got.</summary>
    public ScriptedChatConnector Model => Connector as ScriptedChatConnector ?? throw new InvalidOperationException("This run calls the real model, which keeps no record of its calls.");

    public CapturingLoggerProvider Logs { get; } = new();

    public IServiceProvider Services => _scope.ServiceProvider;

    public IUserDataUnitOfWork UnitOfWork => Services.GetRequiredService<IUserDataUnitOfWork>();

    public async Task<Workflow> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MarkerFile> files = Files ?? [new MarkerFile("annual-report.txt", Encoding.UTF8.GetBytes($"# Annual report\n\n{DocumentText}\n"), MediaType.TextPlain)];
        List<DocumentInfo> documents = [.. files.Select(file =>
        {
            var metadata = new FileMetadata("IniaAi", file.Name, file.Bytes);
            var document = new Document(metadata, file.MediaType) { FileMetadata = metadata };
            return new DocumentInfo(document.Id) { Document = document };
        })];
        Document = documents[0].Document;
        var documentProcessing = _services.GetRequiredService<IDocumentProcessingService>();
        WorkflowHandler handler = _services.GetRequiredService<WorkflowHandler>();

        await documentProcessing.ProcessDocumentsAsync(documents, UnitOfWork, Workflow, cancellationToken: cancellationToken);
        await documentProcessing.LoadContextToWorkflowAsync(documents, Workflow, handler.AllPages, cancellationToken: cancellationToken);
        await handler.CreateRootOutputAsync(UnitOfWork, JobId, Workflow, ORGANIZATION_ID, userId: null);
        ExecutionId = UnitOfWork.WorkflowExecutions.GetAll().Where(execution => execution.JobId == JobId).Select(execution => execution.Id).Single();
        return await handler.ExecuteWorkflowAsync(Workflow, [], ORGANIZATION_ID, JobId, UnitOfWork, inputs: [], cancellationToken: cancellationToken);
    }

    /// <summary>The run's report as its Markdown export, once <see cref="RunAsync"/> has written it.</summary>
    public async Task<string> ReportMarkdownAsync()
    {
        var export = new ExportService(
            NullLogger<ExportService>.Instance,
            UnitOfWork,
            new UserDisplayNameResolver(UnitOfWork),
            [new MarkdownReportWriter()],
            new ChartImageRenderService(Options.Create(new PlatformSettings()), NullLogger<ChartImageRenderService>.Instance),
            new NoReportNotice());
        WorkflowOutputExportResult report = await export.ExportByIdAsync(ExecutionId!, ORGANIZATION_ID, ExportFormat.Markdown);
        return report.Content is null ? string.Empty : Encoding.UTF8.GetString(report.Content.BinaryData.ToArray());
    }

    public void Dispose()
    {
        _scope.Dispose();
        (_services as IDisposable)?.Dispose();
    }
}

/// <summary>One file a <see cref="MarkerRun"/> uploads, as its upload would name and type it.</summary>
public sealed record MarkerFile(string Name, byte[] Bytes, MediaType MediaType);
