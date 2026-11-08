using IniaAi.Testing.Documents;
using System.Text;
using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Core.Workflows;
using IniaAi.FileFormats.Email;
using IniaAi.FileFormats.Inspection;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Harness = IniaAi.Application.UnitTests.Commands.WorkflowExecutions.SubmitWorkflowExecution.SubmitWorkflowExecutionHarness;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.SubmitWorkflowExecution;

public sealed class SubmitWorkflowExecutionEmailExpansionTests : IAsyncLifetime
{
    private const string IDENTIFIER = "Monthly_NAV_2026-05-04_09-30-15";
    private const string BODY = $"email_body_{IDENTIFIER}.pdf";
    private const string RAW = $"{IDENTIFIER}.eml";
    private const string PART = "statement.pdf";
    private const string MESSAGE_ID = "mid-1@fundco.example";
    private const string ATTACHMENT_CLASS = "Attachment";
    private const string BODY_CLASS = "Email Body";
    private const string PLAIN_FILE = "plain.pdf";
    private const string REPORT_CLASS = "Report";
    private const string PLAIN_TEXT = "Plain report";

    private static readonly byte[] _bodyPdf = PdfFixtures.Plain("Email body text");
    private static readonly byte[] _rawEmail = Encoding.ASCII.GetBytes("From: a@b.c\r\nSubject: NAV\r\n\r\nBody\r\n");
    private static readonly byte[] _partPdf = PdfFixtures.Plain("Statement attachment");

    private readonly Harness _harness = new();

    public async ValueTask InitializeAsync()
    {
        await _harness.SeedWorkflowAsync(Harness.WorkflowWith());
    }

    public ValueTask DisposeAsync()
    {
        _harness.Dispose();
        return ValueTask.CompletedTask;
    }

    private static EmailExpansionResult CreateExpansionResult(
        string? messageId = MESSAGE_ID,
        IReadOnlyList<SkippedEmailPart>? skippedParts = null,
        bool bodyShowsContentImage = false,
        byte[]? body = null,
        byte[]? raw = null,
        byte[]? part = null)
    {
        return new EmailExpansionResult(
            IDENTIFIER,
            messageId,
            new ExpandedEmailPart(BODY, body ?? _bodyPdf, "application/pdf"),
            bodyShowsContentImage,
            new ExpandedEmailPart(RAW, raw ?? _rawEmail, "message/rfc822"),
            [new ExpandedEmailPart(PART, part ?? _partPdf, "application/pdf")],
            skippedParts ?? []);
    }

    private static IFormFile CreateEmlFile()
    {
        return Harness.File("monitoring.eml", Encoding.ASCII.GetBytes("From: a@b.c\r\n\r\nBody\r\n"));
    }

    private void ExpandInto(EmailExpansionResult result)
    {
        _ = _harness.EmailExpansion.ExpandAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(result);
    }

    private void FailExpansion()
    {
        _ = _harness.EmailExpansion.ExpandAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns<EmailExpansionResult>(_ => throw new EmailConversionException("conversion failed", new InvalidOperationException("inner")));
    }

    [Fact]
    public async Task Handle_ExpandEmail_UploadsBodyRawAndPartsWithClasses()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [CreateEmlFile()],
            [new SubmitFileMetadata { DocumentClass = BODY_CLASS, ExpandEmail = true, AttachmentsDocumentClass = ATTACHMENT_CLASS }]);

        Assert.False(result.IsError);
        List<PostDocumentInfoRequest> files = _harness.SubmittedFiles();
        Assert.Equal([BODY, RAW, PART], await _harness.FileNamesOfAsync(files.Select(f => f.FileId)));
        Assert.Equal([BODY_CLASS, ATTACHMENT_CLASS, ATTACHMENT_CLASS], files.Select(f => f.DocumentClass));
        Assert.Equal([false, false, false], files.Select(f => f.ShouldUseOcr));
    }

    [Fact]
    public async Task WhileADeployPausesRuns_ASubmissionThatExpandsAnEmail_IsLeftToTheRunsOwnCheck()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult());
        _harness.Intake.Pause(TimeSpan.FromMinutes(30));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [CreateEmlFile()],
            [new SubmitFileMetadata { DocumentClass = BODY_CLASS, ExpandEmail = true, AttachmentsDocumentClass = ATTACHMENT_CLASS }]);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
        Assert.NotEmpty(_harness.SubmittedFiles());
    }

    [Fact]
    public async Task Handle_ExpandEmail_ABodyShowingAPastedTable_GoesThroughOcr()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult(bodyShowsContentImage: true));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [CreateEmlFile()],
            [new SubmitFileMetadata { DocumentClass = BODY_CLASS, ExpandEmail = true, AttachmentsDocumentClass = ATTACHMENT_CLASS }]);

        Assert.False(result.IsError);
        Assert.Equal([true, false, false], _harness.SubmittedFiles().Select(f => f.ShouldUseOcr));
    }

    [Fact]
    public async Task Handle_ExpansionSkips_ReachTheOrchestratorAndTheSubmitResponse()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult(skippedParts: [new SkippedEmailPart("notes.docx", "unsupported type")]));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [CreateEmlFile()],
            [new SubmitFileMetadata { DocumentClass = BODY_CLASS, ExpandEmail = true, AttachmentsDocumentClass = ATTACHMENT_CLASS }]);

        Assert.False(result.IsError);
        Assert.Equal(IDENTIFIER, _harness.SubmittedEmailIdentifier());
        SkippedProtectedFile passed = Assert.Single(_harness.SubmittedIntakeSkips()!);
        Assert.Equal(("notes.docx", "unsupported type"), (passed.FileName, passed.Reason));
        SkippedFileDto reported = Assert.Single(result.Value.IntakeSkippedFiles!);
        Assert.Equal(("notes.docx", "unsupported type"), (reported.FileName, reported.Reason));
    }

    [Fact]
    public async Task Handle_ExpandEmailMixedWithPlainFile_KeepsOrderPlainFirst()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [Harness.Pdf(PLAIN_FILE, PLAIN_TEXT), CreateEmlFile()],
            [
                new SubmitFileMetadata { DocumentClass = REPORT_CLASS },
                new SubmitFileMetadata { DocumentClass = BODY_CLASS, ExpandEmail = true, AttachmentsDocumentClass = ATTACHMENT_CLASS },
            ]);

        Assert.False(result.IsError);
        List<PostDocumentInfoRequest> files = _harness.SubmittedFiles();
        Assert.Equal([PLAIN_FILE, BODY, RAW, PART], await _harness.FileNamesOfAsync(files.Select(f => f.FileId)));
        Assert.Equal(REPORT_CLASS, files[0].DocumentClass);
        Assert.False(files[0].ShouldUseOcr);
    }

    [Fact]
    public async Task Handle_ExpandedAttachmentRefused_SkipsItAndSubmitsTheRest()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult(part: Harness.NotAPdf));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [CreateEmlFile()],
            [new SubmitFileMetadata { ExpandEmail = true, AttachmentsDocumentClass = ATTACHMENT_CLASS }]);

        Assert.False(result.IsError);
        Assert.Equal([BODY, RAW], await _harness.FileNamesOfAsync(_harness.SubmittedFiles().Select(f => f.FileId)));
        SkippedFileDto skip = Assert.Single(result.Value.IntakeSkippedFiles!);
        Assert.Equal(PART, skip.FileName);
        Assert.False(string.IsNullOrWhiteSpace(skip.Reason));
    }

    [Fact]
    public async Task Handle_ExpandedBodyRefused_RejectsWholeSubmit()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult(body: Harness.NotAPdf));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([CreateEmlFile()], [new SubmitFileMetadata { ExpandEmail = true }]);

        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("files[0]", error.Code);
        Assert.StartsWith($"{BODY}: ", error.Description, StringComparison.Ordinal);
        Assert.Empty(await _harness.StoredFileNamesAsync());
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_ExpandedRawEmailRefused_RejectsWholeSubmit()
    {
        SkipUnlessTheBuildReadsEmail();

        byte[] oversizedEmail = [.. _rawEmail, .. Encoding.ASCII.GetBytes(new string('x', (Harness.MAX_FILE_SIZE_MB * 1024 * 1024) + 1))];
        ExpandInto(CreateExpansionResult(raw: oversizedEmail));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([CreateEmlFile()], [new SubmitFileMetadata { ExpandEmail = true }]);

        Error error = Assert.Single(result.Errors);
        Assert.Equal("files[0]", error.Code);
        Assert.Contains(RAW, error.Description, StringComparison.Ordinal);
        Assert.Empty(await _harness.StoredFileNamesAsync());
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_ExpansionConversionFails_RejectsWholeSubmit_AndRemovesTheUploadsMadeBeforeIt()
    {
        SkipUnlessTheBuildReadsEmail();

        FailExpansion();

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [Harness.Pdf(PLAIN_FILE, PLAIN_TEXT), CreateEmlFile()],
            [new SubmitFileMetadata { DocumentClass = REPORT_CLASS }, new SubmitFileMetadata { ExpandEmail = true }]);

        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("File.Email.ConversionFailed", error.Code);
        Assert.Equal(
            "The email could not be opened, so it was not expanded. Save it again from the mail program, then upload the new copy.",
            error.Description);
        Assert.Empty(await _harness.StoredFileNamesAsync());
        Assert.Empty(_harness.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_ExpandEmail_InABuildThatReadsNoEmail_RefusesTheEmailForItsFormat()
    {
        using var openBuild = new Harness(new OpenBuildFormats());
        await openBuild.SeedWorkflowAsync(Harness.WorkflowWith());

        ErrorOr<WorkflowExecutionDto> result = await openBuild.SubmitAsync([CreateEmlFile()], [new SubmitFileMetadata { ExpandEmail = true }]);

        Error error = Assert.Single(result.Errors);
        Assert.Equal("files[0]", error.Code);
        Assert.Equal("monitoring.eml: The platform does not read email files (.eml). Save the email as a PDF, or upload its attachments instead. The full edition reads email files directly and extracts their attachments.", error.Description);
        Assert.Empty(openBuild.EmailExpansion.ReceivedCalls());
        Assert.Empty(await openBuild.StoredFileNamesAsync());
        Assert.Empty(openBuild.Orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_ExpandedEmailWithMessageId_DerivesIdempotencyKeyFromResolvedType()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([CreateEmlFile()], [new SubmitFileMetadata { ExpandEmail = true }]);

        Assert.False(result.IsError);
        string expectedKey = $"email-{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"{Harness.ORGANIZATION_ID}|{Harness.WORKFLOW_TYPE_ID}|{MESSAGE_ID}")))}"[..64];
        Assert.Equal(expectedKey, _harness.SubmittedIdempotencyKey());
    }

    [Fact]
    public async Task Handle_ExplicitIdempotencyKey_WinsOverDerivedKey()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult());

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [CreateEmlFile()],
            [new SubmitFileMetadata { ExpandEmail = true }],
            idempotencyKey: "explicit-key");

        Assert.False(result.IsError);
        Assert.Equal("explicit-key", _harness.SubmittedIdempotencyKey());
    }

    [Fact]
    public async Task Handle_ExpandedEmailWithoutMessageId_PassesNoIdempotencyKey()
    {
        SkipUnlessTheBuildReadsEmail();

        ExpandInto(CreateExpansionResult(messageId: null));

        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync([CreateEmlFile()], [new SubmitFileMetadata { ExpandEmail = true }]);

        Assert.False(result.IsError);
        Assert.Null(_harness.SubmittedIdempotencyKey());
    }

    [Fact]
    public async Task Handle_NoExpansion_PassesNullIdentifierAndNullSkips()
    {
        ErrorOr<WorkflowExecutionDto> result = await _harness.SubmitAsync(
            [Harness.Pdf(PLAIN_FILE, PLAIN_TEXT)],
            [new SubmitFileMetadata { DocumentClass = REPORT_CLASS }]);

        Assert.False(result.IsError);
        Assert.Null(_harness.SubmittedEmailIdentifier());
        Assert.Null(_harness.SubmittedIntakeSkips());
        Assert.Null(result.Value.IntakeSkippedFiles);
    }

    /// <summary>The open build refuses email at intake, so a test that admits one runs on the paid build only.</summary>
    private static void SkipUnlessTheBuildReadsEmail()
    {
        Assert.SkipUnless(FileFormatsUnderTest.ReadsEmail, "This build does not read email.");
    }
}
