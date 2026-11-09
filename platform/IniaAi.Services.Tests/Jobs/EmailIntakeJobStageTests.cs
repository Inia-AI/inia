using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Common.Constants;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Email;
using IniaAi.FileFormats.Unlock;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing.Documents;
using IniaAi.Testing.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Text;

namespace IniaAi.Services.Tests.Jobs;

public class EmailIntakeJobStageTests
{
    private const string ORGANIZATION_ID = EmailIntakeJobStageFixture.ORGANIZATION_ID;
    private const string EXECUTION_ID = EmailIntakeJobStageFixture.EXECUTION_ID;
    private const string IDENTIFIER = EmailIntakeJobStageFixture.IDENTIFIER;

    private readonly EmailIntakeJobStageFixture _fixture = new();
    private readonly EmailIntakeJobStage _stage;

    public EmailIntakeJobStageTests()
    {
        _stage = _fixture.CreateStage(new LoggingIntakeRefusalRecorder(NullLogger<LoggingIntakeRefusalRecorder>.Instance));
    }

    [Fact]
    public async Task ProcessAsync_NoModeKey_DoesNothing()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(mode: null);

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        _ = await _fixture.UnitOfWork.Documents.DidNotReceiveWithAnyArgs()
            .GetByJobIdWithMetadataAsync(default!);
        _ = await _fixture.EmailExpansionService.DidNotReceiveWithAnyArgs().RenderBodyAsync(default!, TestContext.Current.CancellationToken);
        _ = await _fixture.EmailExpansionService.DidNotReceiveWithAnyArgs().ExpandAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_PdfOnly_MintsATextOnlyBodyWithRowClassAndWithoutOcr()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.PDF_ONLY);
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id, documentClass: "Email Body", shouldUseOcr: true);
        _fixture.SetupJobDocuments(job, emailInfo);
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateBodyRenderResult());

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        DocumentInfo bodyInfo = Assert.Single(_fixture.AddedDocumentInfos);
        Assert.Equal(job.Id, bodyInfo.JobId);
        Assert.Equal("Email Body", bodyInfo.DocumentClass);
        Assert.False(bodyInfo.ShouldUseOcr);
        Assert.Equal($"email_body_{IDENTIFIER}", bodyInfo.Document!.Name);
        Assert.Contains(bodyInfo.Id, job.DocumentInfos);
        Assert.Equal(IDENTIFIER, job.Inputs[SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER]);
        Assert.False(job.Inputs.ContainsKey(IntakeInputKeys.EMAIL_INTAKE_MODE));
        Assert.False(job.Inputs.ContainsKey(IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS));
        Assert.Equal("stored/path", bodyInfo.Document!.FileMetadata!.ExternalStoragePath);
        _ = await _fixture.EmailExpansionService.DidNotReceiveWithAnyArgs().ExpandAsync(default!, TestContext.Current.CancellationToken);
        _ = await _fixture.UnitOfWork.Received().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ABodyShowingAPastedTable_IsMintedForOcr_WhateverTheSourceRowSays()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.PDF_ONLY);
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id, shouldUseOcr: false);
        _fixture.SetupJobDocuments(job, emailInfo);
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateBodyRenderResult(bodyShowsContentImage: true));

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        DocumentInfo bodyInfo = Assert.Single(_fixture.AddedDocumentInfos);
        Assert.True(bodyInfo.ShouldUseOcr);
    }

    [Fact]
    public async Task ProcessAsync_ExtractAttachments_MintsBodyAndPartsWithChosenClass()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id, documentClass: "Email Body", shouldUseOcr: true);
        _fixture.SetupJobDocuments(job, emailInfo);
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateExpansionResult());

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        Assert.Equal(3, _fixture.AddedDocumentInfos.Count);

        DocumentInfo bodyInfo = _fixture.AddedDocumentInfos[0];
        Assert.Equal("Email Body", bodyInfo.DocumentClass);
        Assert.False(bodyInfo.ShouldUseOcr);

        foreach (DocumentInfo partInfo in _fixture.AddedDocumentInfos.Skip(1))
        {
            Assert.Equal(job.Id, partInfo.JobId);
            Assert.Equal("Attachment", partInfo.DocumentClass);
            Assert.False(partInfo.ShouldUseOcr);
        }

        Assert.Equal(IDENTIFIER, job.Inputs[SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER]);

        List<SkippedProtectedFile> skips = SkippedProtectedFile.Deserialize(
            job.Inputs[IntakeInputKeys.SKIPPED_PROTECTED_FILES]);
        SkippedProtectedFile skip = Assert.Single(skips);
        Assert.Equal("notes.docx", skip.FileName);
        _ = await _fixture.EmailExpansionService.DidNotReceiveWithAnyArgs().RenderBodyAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_ExistingSkips_MergesNotOverwrites()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        job.Inputs[IntakeInputKeys.SKIPPED_PROTECTED_FILES] = SkippedProtectedFile.Serialize(
            [new SkippedProtectedFile("locked.pdf", "password protected")]);
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateExpansionResult());

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        List<SkippedProtectedFile> skips = SkippedProtectedFile.Deserialize(
            job.Inputs[IntakeInputKeys.SKIPPED_PROTECTED_FILES]);
        Assert.Equal(2, skips.Count);
        Assert.Contains(skips, s => s.FileName == "locked.pdf");
        Assert.Contains(skips, s => s.FileName == "notes.docx");
    }

    [Fact]
    public async Task ProcessAsync_MultipleEmails_FirstIdentifierWins()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.PDF_ONLY);
        DocumentInfo first = _fixture.CreateEmailDocumentInfo(job.Id, fileName: "first.eml");
        DocumentInfo second = _fixture.CreateEmailDocumentInfo(job.Id, fileName: "second.eml", bytes: EmailIntakeJobStageFixture.SecondEmailBytes);
        _fixture.SetupJobDocuments(job, first, second);
        _ = _fixture.EmailExpansionService.DeriveTriggerEmailIdentifierAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns("First_2026-05-04_09-30-15");
        _ = _fixture.EmailExpansionService.DeriveTriggerEmailIdentifierAsync(EmailIntakeJobStageFixture.SecondEmailBytes, Arg.Any<CancellationToken>())
            .Returns("Second_2026-05-04_09-30-15");
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateBodyRenderResult(identifier: "First_2026-05-04_09-30-15"));
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.SecondEmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateBodyRenderResult(identifier: "Second_2026-05-04_09-30-15"));

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        Assert.Equal("First_2026-05-04_09-30-15", job.Inputs[SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER]);
        Assert.Equal(2, _fixture.AddedDocumentInfos.Count);
    }

    [Fact]
    public async Task ProcessAsync_NonEmailDocsOnly_ConsumesKeysAndMintsNothing()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        var pdfMetadata = new FileMetadata(ORGANIZATION_ID, "report.pdf", 10, StorageProvider.Database, "pdf/path");
        var pdfDocument = new Document(pdfMetadata, MediaType.ApplicationPdf);
        var pdfInfo = new DocumentInfo(pdfDocument.Id)
        {
            Document = pdfDocument,
            JobId = job.Id,
            DocumentClass = "Report",
        };
        _fixture.SetupJobDocuments(job, pdfInfo);

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        Assert.Empty(_fixture.AddedDocumentInfos);
        Assert.False(job.Inputs.ContainsKey(IntakeInputKeys.EMAIL_INTAKE_MODE));
        _ = await _fixture.EmailExpansionService.DidNotReceiveWithAnyArgs().ExpandAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_MintedDocuments_AreCountedAsTheyAreMade()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateExpansionResult(
                new ExpandedEmailPart("email_body.pdf", PdfFixtures.Pages("Body"), "application/pdf"),
                new ExpandedEmailPart("statement.pdf", PdfFixtures.Pages("One", "Two"), "application/pdf"),
                new ExpandedEmailPart("nav-data.csv", Encoding.ASCII.GetBytes("fund,nav\nAlpha,101.2"), "text/csv")));

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 1], _fixture.AddedDocumentInfos.Select(info => info.Document!.CountedPages));
    }

    [Fact]
    public async Task ProcessAsync_MintedDocuments_CarryTriggerIdentifierSource()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.PDF_ONLY);
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateBodyRenderResult());

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        DocumentInfo bodyInfo = Assert.Single(_fixture.AddedDocumentInfos);
        string source = Assert.Single(bodyInfo.Document!.Sources);
        Assert.Equal(IDENTIFIER, source);
    }

    [Fact]
    public async Task ProcessAsync_ProductsAlreadyInJob_SkipsWithoutReprocessing()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        var mintedBody = new Document(new FileMetadata(
            ORGANIZATION_ID,
            $"email_body_{IDENTIFIER}.pdf",
            EmailIntakeJobStageFixture.BodyPdfBytes.Length,
            StorageProvider.Database,
            "body/path"), MediaType.ApplicationPdf)
        {
            Sources = [IDENTIFIER],
        };
        var mintedInfo = new DocumentInfo(mintedBody.Id)
        {
            Document = mintedBody,
            JobId = job.Id,
        };
        _fixture.SetupJobDocuments(job, emailInfo, mintedInfo);

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        Assert.Empty(_fixture.AddedDocumentInfos);
        Assert.Equal(IDENTIFIER, job.Inputs[SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER]);
        Assert.False(job.Inputs.ContainsKey(IntakeInputKeys.EMAIL_INTAKE_MODE));
        Assert.False(job.Inputs.ContainsKey(IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS));
        _ = await _fixture.EmailExpansionService.DidNotReceiveWithAnyArgs().ExpandAsync(default!, TestContext.Current.CancellationToken);
        _ = await _fixture.EmailExpansionService.DidNotReceiveWithAnyArgs().RenderBodyAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_LegacyProductsWithoutIdentifier_DoesNotDuplicateAndHealsMarker()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.PDF_ONLY);
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        var legacyBody = new Document(new FileMetadata(
            ORGANIZATION_ID,
            $"email_body_{IDENTIFIER}.pdf",
            EmailIntakeJobStageFixture.BodyPdfBytes.Length,
            StorageProvider.Database,
            "body/path"), MediaType.ApplicationPdf)
        {
            Sources = ["Email Intake"],
        };
        var legacyInfo = new DocumentInfo(legacyBody.Id)
        {
            Document = legacyBody,
            JobId = job.Id,
        };
        _fixture.SetupJobDocuments(job, emailInfo, legacyInfo);
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateBodyRenderResult());
        _ = _fixture.UnitOfWork.Documents.GetOrCreateDocumentAsync(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<StorageProvider>(),
                Arg.Any<MediaType>())
            .Returns((legacyBody, false));

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        Assert.Empty(_fixture.AddedDocumentInfos);
        Assert.Contains("Email Intake", legacyBody.Sources);
        Assert.Contains(IDENTIFIER, legacyBody.Sources);
        Assert.Equal(IDENTIFIER, job.Inputs[SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER]);
    }

    [Fact]
    public async Task ProcessAsync_SameEmailTwiceInOneJob_MintsOnce()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.PDF_ONLY);
        DocumentInfo first = _fixture.CreateEmailDocumentInfo(job.Id, fileName: "monitoring.eml");
        DocumentInfo duplicate = _fixture.CreateEmailDocumentInfo(job.Id, fileName: "monitoring-copy.eml");
        _fixture.SetupJobDocuments(job, first, duplicate);
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateBodyRenderResult());

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        _ = Assert.Single(_fixture.AddedDocumentInfos);
        _ = await _fixture.EmailExpansionService.Received(1).RenderBodyAsync(
            Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_BodyConversionFails_AppendsIntakeErrorAndThrows()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.PDF_ONLY);
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .ThrowsAsync(new EmailConversionException("render failed", new InvalidDataException("bad bytes")));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken));

        await _fixture.WorkflowOutputService.Received(1).AppendDocumentIntakeErrorLogAsync(
            EXECUTION_ID,
            "monitoring.eml",
            Arg.Any<string>());
    }

    [Fact]
    public async Task ProcessAsync_DedupedDocumentWithStoragePath_DoesNotStoreAgain()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.PDF_ONLY);
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        _ = _fixture.UnitOfWork.Documents.GetOrCreateDocumentAsync(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<StorageProvider>(),
                Arg.Any<MediaType>())
            .Returns(callInfo => (new Document(new FileMetadata(
                callInfo.ArgAt<string>(1),
                callInfo.ArgAt<string>(3),
                10,
                StorageProvider.Database,
                "already/stored"), Utils.DetermineMediaTypeFromExtension(Path.GetExtension(callInfo.ArgAt<string>(3)))), false));
        _ = _fixture.EmailExpansionService.RenderBodyAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(EmailIntakeJobStageFixture.CreateBodyRenderResult());

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        _ = await _fixture.FileStorageManager.DidNotReceiveWithAnyArgs().StoreFileAsync(
            default(string)!, default!, default!, default!, default(Stream)!, default!);
    }

    [Fact]
    public async Task ProcessAsync_PartWithStrippedProtection_StoresDecryptedCopyFlagsAndAudits()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);

        byte[] encryptedBytes = PdfFixtures.Minimal("still encrypted");
        byte[] decryptedBytes = PdfFixtures.Minimal("decrypted copy");
        EmailExpansionResult expansion = EmailIntakeJobStageFixture.CreateExpansionResult(
            EmailIntakeJobStageFixture.CreateBodyPart(IDENTIFIER),
            new ExpandedEmailPart(
                "restricted.pdf",
                encryptedBytes,
                "application/pdf",
                new FileUnlockResult(
                    FileProtectionStatus.Decrypted,
                    decryptedBytes,
                    WasPasswordProtected: true,
                    ContainedSignature: true)));
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(expansion);

        List<byte[]> mintedBytes = [];
        _ = _fixture.UnitOfWork.Documents.GetOrCreateDocumentAsync(
                Arg.Do<byte[]>(mintedBytes.Add),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<StorageProvider>(),
                Arg.Any<MediaType>())
            .Returns(callInfo => (new Document(
                new FileMetadata(callInfo.ArgAt<string>(1), callInfo.ArgAt<string>(3), callInfo.ArgAt<byte[]>(0)),
                callInfo.ArgAt<MediaType>(6)), true));

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        DocumentInfo partInfo = Assert.Single(_fixture.AddedDocumentInfos, i => i.Document!.Name == "restricted");
        Assert.True(partInfo.Document!.WasPasswordProtected);

        DocumentInfo bodyInfo = Assert.Single(_fixture.AddedDocumentInfos, i => i.Document!.Name == $"email_body_{IDENTIFIER}");
        Assert.False(bodyInfo.Document!.WasPasswordProtected);

        Assert.Contains(decryptedBytes, mintedBytes);
        Assert.DoesNotContain(encryptedBytes, mintedBytes);

        _fixture.UnitOfWork.Repository<DocumentUnlockAuditLog>().Received(1).Add(
            Arg.Is<DocumentUnlockAuditLog>(l =>
                l.DocumentId == partInfo.DocumentId &&
                !l.PasswordSupplied &&
                l.ContainedSignature &&
                l.ActorUserId == "user-1"));
    }

    [Fact]
    public async Task ProcessAsync_MacroWorkbookAttachment_IsStoredWithItsMacroMark()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        EmailExpansionResult expansion = EmailIntakeJobStageFixture.CreateExpansionResult(
            EmailIntakeJobStageFixture.CreateBodyPart(IDENTIFIER),
            new ExpandedEmailPart("model.xlsm", OoxmlFixtures.MacroWorkbook(), null));
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(expansion);

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        DocumentInfo workbookInfo = Assert.Single(_fixture.AddedDocumentInfos, i => i.Document!.Name == "model");
        Assert.True(workbookInfo.Document!.HasMacros);
        DocumentInfo bodyInfo = Assert.Single(_fixture.AddedDocumentInfos, i => i.Document!.Name == $"email_body_{IDENTIFIER}");
        Assert.False(bodyInfo.Document!.HasMacros);
    }

    [Fact]
    public async Task ProcessAsync_HostileAttachment_SkipsItWithItsReason()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        byte[] executable = HostileBytes.WindowsExecutable();
        EmailExpansionResult expansion = EmailIntakeJobStageFixture.CreateExpansionResult(
            EmailIntakeJobStageFixture.CreateBodyPart(IDENTIFIER),
            new ExpandedEmailPart("invoice.pdf", executable, "application/pdf"));
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(expansion);

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        DocumentInfo bodyInfo = Assert.Single(_fixture.AddedDocumentInfos);
        Assert.Equal($"email_body_{IDENTIFIER}", bodyInfo.Document!.Name);
        SkippedProtectedFile skip = Assert.Single(SkippedProtectedFile.Deserialize(job.Inputs[IntakeInputKeys.SKIPPED_PROTECTED_FILES]));
        Assert.Equal("invoice.pdf", skip.FileName);
        Assert.Contains("accept", skip.Reason);
        _ = await _fixture.FileStorageManager.DidNotReceive().StoreFileAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Is<Stream>(s => s.Length == executable.Length), Arg.Any<string>());
    }

    [Fact]
    public async Task ProcessAsync_HostileAttachmentOnAJobWhoseInputIsVouchedFor_IsMintedAsTheTypeItsNameClaims()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        job.InputIsVouchedFor = true;
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        EmailExpansionResult expansion = EmailIntakeJobStageFixture.CreateExpansionResult(
            EmailIntakeJobStageFixture.CreateBodyPart(IDENTIFIER),
            new ExpandedEmailPart("invoice.pdf", HostileBytes.WindowsExecutable(), "application/pdf"));
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(expansion);

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        DocumentInfo partInfo = Assert.Single(_fixture.AddedDocumentInfos, i => i.Document!.Name == "invoice");
        Assert.Equal(MediaType.ApplicationPdf, partInfo.Document!.MediaType);
        Assert.False(job.Inputs.ContainsKey(IntakeInputKeys.SKIPPED_PROTECTED_FILES));
    }

    [Fact]
    public async Task ProcessAsync_RefusedBody_FailsTheEmailAndMintsNothing()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        ExpandedEmailPart hostileBody = EmailIntakeJobStageFixture.CreateBodyPart(IDENTIFIER) with { Content = HostileBytes.WindowsExecutable() };
        EmailExpansionResult expansion = EmailIntakeJobStageFixture.CreateExpansionResult(hostileBody);
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(expansion);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken));

        Assert.Contains("not admitted", failure.InnerException!.Message);
        Assert.Empty(_fixture.AddedDocumentInfos);
    }

    [Fact]
    public async Task ProcessAsync_MintedPart_CarriesTheDetectedTypeNotTheName()
    {
        Job job = EmailIntakeJobStageFixture.CreateJob(EmailIntakeModes.EXTRACT_ATTACHMENTS, attachmentsClass: "Attachment");
        DocumentInfo emailInfo = _fixture.CreateEmailDocumentInfo(job.Id);
        _fixture.SetupJobDocuments(job, emailInfo);
        EmailExpansionResult expansion = EmailIntakeJobStageFixture.CreateExpansionResult(
            EmailIntakeJobStageFixture.CreateBodyPart(IDENTIFIER),
            new ExpandedEmailPart("report.exe", PdfFixtures.Minimal("renamed report"), "application/octet-stream"));
        _ = _fixture.EmailExpansionService.ExpandAsync(EmailIntakeJobStageFixture.EmailBytes, Arg.Any<CancellationToken>())
            .Returns(expansion);

        await _stage.ProcessAsync(job, EXECUTION_ID, _fixture.UnitOfWork, TestContext.Current.CancellationToken);

        DocumentInfo partInfo = Assert.Single(_fixture.AddedDocumentInfos, i => i.Document!.Name == "report");
        Assert.Equal(MediaType.ApplicationPdf, partInfo.Document!.MediaType);
        _ = await _fixture.FileStorageManager.Received().StoreFileAsync(
            Arg.Any<string>(), ORGANIZATION_ID, StorageCategories.Documents, partInfo.Document.FileMetadata!.Id, Arg.Any<Stream>(), "application/pdf");
    }
}
