using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Common.Constants;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Email;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text;

namespace IniaAi.Services.Tests.Jobs;

/// <summary>
/// The substitutes and builders every email-intake stage test shares: storage that stores,
/// a unit of work that mints documents and records the infos it was asked to create, and one
/// job per test. The recorder is the caller's choice, so the platform's logging trail and the
/// screening record are tested against the same stage.
/// </summary>
internal sealed class EmailIntakeJobStageFixture
{
    public const string ORGANIZATION_ID = "org-1";
    public const string EXECUTION_ID = "execution-1";
    public const string IDENTIFIER = "Monthly_NAV_2026-05-04_09-30-15";
    private const string EMAIL_STORAGE_PATH = "email/path";

    public static readonly byte[] EmailBytes = Encoding.ASCII.GetBytes("raw email bytes");
    public static readonly byte[] SecondEmailBytes = Encoding.ASCII.GetBytes("second raw email bytes");
    public static readonly byte[] BodyPdfBytes = PdfFixtures.Minimal("body");

    public IEmailExpansionService EmailExpansionService { get; } = Substitute.For<IEmailExpansionService>();
    public IFileStorageManager FileStorageManager { get; } = Substitute.For<IFileStorageManager>();
    public IWorkflowOutputService WorkflowOutputService { get; } = Substitute.For<IWorkflowOutputService>();
    public IUserDataUnitOfWork UnitOfWork { get; } = Substitute.For<IUserDataUnitOfWork>();
    public List<DocumentInfo> AddedDocumentInfos { get; } = [];

    public EmailIntakeJobStageFixture()
    {
        _ = FileStorageManager.GetProviderForTenant(Arg.Any<string>()).Returns(StorageProvider.Database);
        _ = FileStorageManager.StoreFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>())
            .Returns("stored/path");
        _ = EmailExpansionService.DeriveTriggerEmailIdentifierAsync(
                Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(IDENTIFIER);

        _ = UnitOfWork.Documents.GetOrCreateDocumentAsync(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<StorageProvider>(),
                Arg.Any<MediaType>())
            .Returns(callInfo => (new Document(
                new FileMetadata(callInfo.ArgAt<string>(1), callInfo.ArgAt<string>(3), callInfo.ArgAt<byte[]>(0)),
                callInfo.ArgAt<MediaType>(6)), true));
        _ = UnitOfWork.Documents.CreateDocumentInfoAsync(Arg.Any<Document>())
            .Returns(callInfo =>
            {
                var documentInfo = new DocumentInfo(callInfo.Arg<Document>().Id)
                {
                    Document = callInfo.Arg<Document>(),
                };
                AddedDocumentInfos.Add(documentInfo);
                return documentInfo;
            });
    }

    public EmailIntakeJobStage CreateStage(IIntakeRefusalRecorder refusalRecorder)
    {
        var intakeGate = new DocumentIntakeGate(
            refusalRecorder,
            FileFormatsUnderTest.Unlocker,
            FileFormatsUnderTest.ReadableFormats,
            Options.Create(new PlatformSettings { IniaAi = new IniaAiSettings { MaxFileSizeMb = 10 } }));

        return new EmailIntakeJobStage(
            EmailExpansionService,
            FileStorageManager,
            WorkflowOutputService,
            intakeGate,
            FileFormatsUnderTest.PageCounter,
            NullLogger<EmailIntakeJobStage>.Instance);
    }

    public static Job CreateJob(string? mode, string? attachmentsClass = null)
    {
        var job = new Job(ORGANIZATION_ID, "HedgeFundsMonitoring");
        job.SetUserId("user-1");

        if (mode is not null)
        {
            job.Inputs[IntakeInputKeys.EMAIL_INTAKE_MODE] = mode;
        }

        if (attachmentsClass is not null)
        {
            job.Inputs[IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS] = attachmentsClass;
        }

        return job;
    }

    public DocumentInfo CreateEmailDocumentInfo(
        string jobId,
        string fileName = "monitoring.eml",
        string documentClass = "Email Body",
        bool shouldUseOcr = true,
        byte[]? bytes = null)
    {
        byte[] storedBytes = bytes ?? EmailBytes;
        string storagePath = $"{EMAIL_STORAGE_PATH}/{fileName}";
        _ = FileStorageManager.RetrieveFileBytesAsync(
                Arg.Any<string>(),
                ORGANIZATION_ID,
                StorageCategories.Documents,
                storagePath)
            .Returns(storedBytes);

        var fileMetadata = new FileMetadata(
            ORGANIZATION_ID,
            fileName,
            storedBytes.Length,
            StorageProvider.Database,
            storagePath);
        var document = new Document(fileMetadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(fileName)));

        return new DocumentInfo(document.Id)
        {
            Document = document,
            JobId = jobId,
            DocumentClass = documentClass,
            ShouldUseOcr = shouldUseOcr,
        };
    }

    public void SetupJobDocuments(Job job, params DocumentInfo[] documentInfos)
    {
        _ = UnitOfWork.Documents.GetByJobIdWithMetadataAsync(job.Id)
            .Returns([.. documentInfos]);
    }

    public static ExpandedEmailPart CreateBodyPart(string identifier)
    {
        return new ExpandedEmailPart($"email_body_{identifier}.pdf", BodyPdfBytes, "application/pdf");
    }

    public static EmailBodyRenderResult CreateBodyRenderResult(string identifier = IDENTIFIER, bool bodyShowsContentImage = false)
    {
        return new EmailBodyRenderResult(identifier, CreateBodyPart(identifier), bodyShowsContentImage);
    }

    public static EmailExpansionResult CreateExpansionResult(string identifier = IDENTIFIER)
    {
        return CreateExpansionResult(
            identifier,
            CreateBodyPart(identifier),
            [
                new ExpandedEmailPart("statement.pdf", PdfFixtures.Minimal("part"), "application/pdf"),
                new ExpandedEmailPart("nav-data.csv", Encoding.ASCII.GetBytes("fund,nav"), "text/csv"),
            ],
            [new SkippedEmailPart("notes.docx", "Word attachments are not supported.")]);
    }

    public static EmailExpansionResult CreateExpansionResult(ExpandedEmailPart body, params ExpandedEmailPart[] parts)
    {
        return CreateExpansionResult(IDENTIFIER, body, parts, []);
    }

    private static EmailExpansionResult CreateExpansionResult(
        string identifier,
        ExpandedEmailPart body,
        ExpandedEmailPart[] parts,
        SkippedEmailPart[] skipped)
    {
        return new EmailExpansionResult(
            identifier,
            "message-id-1",
            body,
            BodyShowsContentImage: false,
            new ExpandedEmailPart($"{identifier}.eml", EmailBytes, "message/rfc822"),
            [.. parts],
            [.. skipped]);
    }
}
