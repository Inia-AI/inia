using IniaAi.FileFormats;
using IniaAi.FileFormats.Inspection;
using IniaAi.Testing.Documents;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Commands.WorkflowExecutions.SubmitWorkflowExecution;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.SubmitWorkflowExecution;

/// <summary>
/// The submit handler over a real database, with its uploads and clean-up sent through the real
/// dispatcher to the real upload and delete handlers. Only the orchestrator, which starts the run,
/// and the email expansion are stand-ins, so each test can read what the submit handed them.
/// </summary>
internal sealed class SubmitWorkflowExecutionHarness : IDisposable
{
    public const string ORGANIZATION_ID = "org-1";
    public const string USER_ID = "user-1";
    public const string USERNAME = "test";
    public const string EMAIL = "test@test.com";
    public const string WORKFLOW_ID = "wf-1";
    public const string WORKFLOW_TYPE_ID = "wf-type-1";
    public const int MAX_FILE_SIZE_MB = 1;

    // A PDF records the second it was built, so only one copy per text keeps two uploads of it identical.
    private static readonly ConcurrentDictionary<string, byte[]> _pdfs = new();

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public SubmitWorkflowExecutionHarness(IReadableFormats? readableFormats = null)
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddTestDatabaseServices();
        _ = services.AddTestFileStorage();
        _ = services.AddSingleton<IIntakeRefusalRecorder, LoggingIntakeRefusalRecorder>();
        _ = services.AddSingleton(Options.Create(new PlatformSettings { IniaAi = new IniaAiSettings { MaxFileSizeMb = MAX_FILE_SIZE_MB } }));
        _ = services.AddFileFormatsUnderTest();
        if (readableFormats is not null)
        {
            _ = services.AddSingleton(readableFormats);
        }

        _ = services.AddSingleton<DocumentIntakeGate>();
        _ = services.AddSingleton(Orchestrator);
        _ = services.AddSingleton(EmailExpansion);
        _ = services.AddApplication();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        _ = Orchestrator.ExecuteWorkflowAsync(default!, default, default, default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new WorkflowExecutionDto { Id = "exec-1" });
        _ = EmailExpansion.CountPagesAsync(default!, default)
            .ReturnsForAnyArgs(new EmailPageCount("Submitted_email_2026-05-04_09-30-15", BodyPages: 1, AttachmentPages: 0));
    }

    public static byte[] NotAPdf { get; } = Encoding.ASCII.GetBytes("This text is not a PDF document.");

    public IWorkflowExecutionOrchestrator Orchestrator { get; } = Substitute.For<IWorkflowExecutionOrchestrator>();

    public IEmailExpansionService EmailExpansion { get; } = Substitute.For<IEmailExpansionService>();

    public RunIntake Intake => _provider.GetRequiredService<RunIntake>();

    public static IFormFile File(string fileName, byte[] content)
    {
        return FormFileTestHelper.CreateFromBytes(content, fileName);
    }

    public static IFormFile Pdf(string fileName, string text = "Submitted report")
    {
        return File(fileName, _pdfs.GetOrAdd(text, PdfFixtures.Plain));
    }

    public static Workflow WorkflowWith(params (string Tag, string? DefaultValue, bool IsRequired, WorkflowVariableTypes Type)[] variables)
    {
        Workflow workflow = new() { Id = WORKFLOW_ID, WorkflowTypeId = WORKFLOW_TYPE_ID };
        foreach ((string tag, string? defaultValue, bool isRequired, WorkflowVariableTypes type) in variables)
        {
            workflow.Variables.Add(new WorkflowVariable(workflow, tag)
            {
                DefaultValue = defaultValue,
                IsRequired = isRequired,
                Type = type,
            });
        }

        return workflow;
    }

    public async Task SeedWorkflowAsync(Workflow workflow)
    {
        await SeedWorkflowWithoutAccessAsync(workflow);
        CoreDbContext core = _scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        core.GrantWorkflowTypeAccess(workflow.WorkflowTypeId!, ORGANIZATION_ID);
        _ = await core.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async Task SeedWorkflowWithoutAccessAsync(Workflow workflow)
    {
        CoreDbContext core = _scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.Workflows.Add(workflow);
        _ = await core.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async Task<FileDto> UploadAsync(IFormFile file)
    {
        ErrorOr<FileDto> uploaded = await _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new UploadFileCommand(file, ORGANIZATION_ID, USER_ID), TestContext.Current.CancellationToken);

        return uploaded.Value;
    }

    public Task<ErrorOr<WorkflowExecutionDto>> SubmitAsync(
        IReadOnlyList<IFormFile> files,
        List<SubmitFileMetadata>? fileMetadata = null,
        Dictionary<string, JsonElement>? variables = null,
        string? idempotencyKey = null)
    {
        return SubmitAsync(
            new SubmitWorkflowExecutionRequest { WorkflowTypeId = WORKFLOW_TYPE_ID, Files = fileMetadata, Variables = variables },
            files,
            idempotencyKey);
    }

    public async Task<ErrorOr<WorkflowExecutionDto>> SubmitAsync(
        SubmitWorkflowExecutionRequest metadata,
        IReadOnlyList<IFormFile> files,
        string? idempotencyKey = null)
    {
        SubmitWorkflowExecutionCommandHandler handler = ActivatorUtilities.CreateInstance<SubmitWorkflowExecutionCommandHandler>(_scope.ServiceProvider);

        return await handler.Handle(
            new SubmitWorkflowExecutionCommand(ORGANIZATION_ID, USER_ID, USERNAME, EMAIL, metadata, files, IdempotencyKey: idempotencyKey),
            TestContext.Current.CancellationToken);
    }

    public Workflow SubmittedWorkflow()
    {
        return OrchestratorArgument<Workflow>("workflow");
    }

    public List<PostDocumentInfoRequest> SubmittedFiles()
    {
        return OrchestratorArgument<List<PostDocumentInfoRequest>>("files");
    }

    public Dictionary<string, JsonElement> SubmittedVariables()
    {
        return OrchestratorArgument<Dictionary<string, JsonElement>>("variables");
    }

    public (string OrganizationId, string UserId, string? Username, string? Email, string? CounterpartyId, bool IsConfidential) SubmittedRequester()
    {
        return (
            OrchestratorArgument<string>("organizationId"),
            OrchestratorArgument<string>("userId"),
            OrchestratorArgument<string?>("username"),
            OrchestratorArgument<string?>("email"),
            OrchestratorArgument<string?>("counterpartyId"),
            OrchestratorArgument<bool>("isConfidential"));
    }

    public string? SubmittedIdempotencyKey()
    {
        return OrchestratorArgument<string?>("idempotencyKey");
    }

    public string? SubmittedEmailIdentifier()
    {
        return OrchestratorArgument<string?>("triggerEmailIdentifier");
    }

    public IReadOnlyList<SkippedProtectedFile>? SubmittedIntakeSkips()
    {
        return OrchestratorArgument<IReadOnlyList<SkippedProtectedFile>?>("intakeSkippedFiles");
    }

    public async Task<List<string>> FileNamesOfAsync(IEnumerable<string> documentIds)
    {
        List<string> names = [];
        foreach (string documentId in documentIds)
        {
            Document document = await Documents().SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
            names.Add(document.FileName);
        }

        return names;
    }

    public async Task<List<List<string>>> SourcesOfAsync(IEnumerable<string> documentIds)
    {
        List<List<string>> sources = [];
        foreach (string documentId in documentIds)
        {
            Document document = await Documents().SingleAsync(d => d.Id == documentId, TestContext.Current.CancellationToken);
            sources.Add(document.Sources);
        }

        return sources;
    }

    public async Task<List<string>> StoredFileNamesAsync()
    {
        List<Document> documents = await Documents().ToListAsync(TestContext.Current.CancellationToken);

        return [.. documents.Select(d => d.FileName).Order()];
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private T OrchestratorArgument<T>(string parameterName)
    {
        NSubstitute.Core.ICall call = Orchestrator.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IWorkflowExecutionOrchestrator.ExecuteWorkflowAsync));
        int position = call.GetMethodInfo().GetParameters().Single(p => p.Name == parameterName).Position;

        return (T)call.GetArguments()[position]!;
    }

    private IQueryable<Document> Documents()
    {
        return _scope.ServiceProvider.GetRequiredService<UserDataDbContext>().Documents.AsNoTracking().Include(d => d.FileMetadata);
    }
}
