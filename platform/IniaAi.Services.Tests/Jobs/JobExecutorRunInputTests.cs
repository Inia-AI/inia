using IniaAi.Common.Enums;
using IniaAi.Core.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace IniaAi.Services.Tests.Jobs;

/// <summary>
/// The run input a job processes, against a real database: the executor hands every loaded
/// document to the run-input stage with the execution the run belongs to, and processes only what
/// the stage left in the run.
/// </summary>
public sealed class JobExecutorRunInputTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-run-input";
    private const string WORKFLOW_TYPE_ID = "RunInputType";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

    public JobExecutorRunInputTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task TheRunInput_IsWhatTheStageLeftInTheRun()
    {
        var stage = new RecordingStage();
        Job job = CreateJob();
        job.WorkflowExecutionId = "execution-api-mouth";
        string kept = await SeedDocumentInfoAsync(job, "statement.pdf");
        stage.Exclude = await SeedDocumentInfoAsync(job, "noise.pdf");

        IReadOnlyList<DocumentInfo> runInput = await CreateExecutor(stage).LoadRunInputAsync(job, UnitOfWork, TestContext.Current.CancellationToken);

        Assert.Equal(1, stage.Calls);
        Assert.Equal("execution-api-mouth", stage.ExecutionId);
        Assert.Equal(2, stage.Given);
        Assert.Equal(kept, Assert.Single(runInput).DocumentId);
    }

    [Fact]
    public async Task ThePlatformStage_KeepsEveryDocumentTheRunLoads()
    {
        Job job = CreateJob();
        _ = await SeedDocumentInfoAsync(job, "statement.pdf");
        _ = await SeedDocumentInfoAsync(job, "noise.pdf");

        IReadOnlyList<DocumentInfo> runInput = await CreateExecutor(new PassThroughRunInputStage())
            .LoadRunInputAsync(job, UnitOfWork, TestContext.Current.CancellationToken);

        Assert.Equal(2, runInput.Count);
    }

    private IUserDataUnitOfWork UnitOfWork => _provider.GetRequiredService<IUserDataUnitOfWork>();

    private JobExecutor CreateExecutor(IRunInputStage runInputStage)
    {
        return new JobExecutor(
            NullLogger<JobExecutor>.Instance,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            documentProcessing: null!,
            workflowOutputService: null!,
            emailIntakeJobStage: null!,
            runInputStage: runInputStage);
    }

    private static Job CreateJob()
    {
        return new Job(ORGANIZATION_ID, WORKFLOW_TYPE_ID) { Workflow = new Workflow { WorkflowTypeId = WORKFLOW_TYPE_ID } };
    }

    private async Task<string> SeedDocumentInfoAsync(Job job, string fileName)
    {
        var document = new Document(new FileMetadata(ORGANIZATION_ID, fileName, Encoding.UTF8.GetBytes(fileName)), MediaType.ApplicationPdf);
        UserDataDbContext context = _provider.GetRequiredService<UserDataDbContext>();
        _ = await context.DocumentInfos.AddAsync(new DocumentInfo(document.Id) { Document = document, JobId = job.Id }, TestContext.Current.CancellationToken);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return document.Id;
    }

    private sealed class RecordingStage : IRunInputStage
    {
        public int Calls { get; private set; }

        public int Given { get; private set; }

        public string? ExecutionId { get; private set; }

        public string? Exclude { get; set; }

        public Task RunAsync(
            Job job,
            string? workflowExecutionId,
            IReadOnlyList<DocumentInfo> documentInfos,
            IUserDataUnitOfWork userDataUnitOfWork,
            CancellationToken cancellationToken)
        {
            Calls++;
            Given = documentInfos.Count;
            ExecutionId = workflowExecutionId;

            foreach (DocumentInfo documentInfo in documentInfos.Where(documentInfo => documentInfo.DocumentId == Exclude))
            {
                documentInfo.ExcludeFromRun("NotNeeded", DateTime.UtcNow);
            }

            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
