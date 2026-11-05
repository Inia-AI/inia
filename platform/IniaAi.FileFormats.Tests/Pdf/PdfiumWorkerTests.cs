using System.Diagnostics;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Pdf.Pdfium;
using IniaAi.FileFormats.Unlock;
using IniaAi.Testing;
using IniaAi.Testing.Documents;

namespace IniaAi.FileFormats.Tests.Pdf;

public class PdfiumWorkerTests
{
    private const string SHELL = "/bin/sh";
    private const string SECRET_VARIABLE = "INIA_PDF_WORKER_SECRET_PROBE";
    private static readonly TimeSpan _answerTimeLimit = TimeSpan.FromSeconds(30);

    [Fact]
    public void Open_OnAWorkerThatStops_FailsThatDocumentWithTheExitCode_AndDisposesTheStream()
    {
        SkipWithoutAShell();
        MemoryStream stream = ContractFixture.Stream(ContractFixture.THREE_PAGES_PDF);

        PdfiumWorkerException failure = Assert.Throws<PdfiumWorkerException>(
            () => FakeWorker("exit 7").Open(stream, "stops.pdf"));

        Assert.Contains("stopped while reading stops.pdf (exit code 7)", failure.Message, StringComparison.Ordinal);
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task Open_OnAWorkerThatStopsButLeavesAChildHoldingItsErrorOutput_StillFailsThatDocument_AndFreesItsWorker()
    {
        SkipWithoutAShell();
        var workers = new PdfiumWorkerLimit(1);
        var format = new PdfiumPdfFormat(workers, _answerTimeLimit, new PdfiumWorkerCommand(SHELL, ["-c", "sleep 5 >/dev/null & exit 7"]));

        PdfiumWorkerException failure = await Task.Run(
            () => Assert.Throws<PdfiumWorkerException>(() => format.Open(ContractFixture.Stream(ContractFixture.THREE_PAGES_PDF), "forks.pdf")),
            TestContext.Current.CancellationToken).WaitAsync(_answerTimeLimit, TestContext.Current.CancellationToken);

        Assert.Contains("(exit code 7)", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, workers.Live);
    }

    [Fact]
    public void Open_OnAWorkerThatNeverAnswers_StopsItAtTheTimeLimit_AndFailsThatDocument()
    {
        SkipWithoutAShell();
        var format = new PdfiumPdfFormat(new PdfiumWorkerLimit(1), TimeSpan.FromMilliseconds(300), new PdfiumWorkerCommand(SHELL, ["-c", "cat > /dev/null"]));

        PdfiumWorkerException failure = Assert.Throws<PdfiumWorkerException>(
            () => format.Open(ContractFixture.Stream(ContractFixture.THREE_PAGES_PDF), "hangs.pdf"));

        Assert.Contains("did not answer within", failure.Message, StringComparison.Ordinal);
        Assert.Contains("hangs.pdf", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_StartsWithoutTheServersEnvironment()
    {
        SkipWithoutAShell();
        Environment.SetEnvironmentVariable(SECRET_VARIABLE, "not for the worker");
        try
        {
            PdfiumWorkerException failure = Assert.Throws<PdfiumWorkerException>(
                () => FakeWorker($"[ -z \"${SECRET_VARIABLE}\" ] && exit 21 || exit 22").Open(ContractFixture.Stream(ContractFixture.THREE_PAGES_PDF), "secret.pdf"));

            Assert.Contains("(exit code 21)", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SECRET_VARIABLE, null);
        }
    }

    [Fact]
    public void AWorkerKilledMidDocument_FailsOnlyThatDocument_AndTheNextDocumentReadsOnANewWorker()
    {
        var format = new PdfiumPdfFormat();
        using IPdfDocument killed = Open(format);
        using IPdfDocument other = Open(format);
        Process.GetProcessById(((PdfiumPdfDocument)killed).WorkerProcessId).Kill();

        PdfiumWorkerException failure = Assert.Throws<PdfiumWorkerException>(() => killed.Text(2));

        Assert.Contains("stopped while reading", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Bravo page", other.Text(2), StringComparison.Ordinal);
        using IPdfDocument next = Open(format);
        Assert.Contains("Charlie page", next.Text(3), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unlock_OnAWorkerThatStops_HasUnsupportedProtection_AsThePaidUnlockerAnswers()
    {
        SkipWithoutAShell();
        var unlocker = new OpenDocumentUnlocker(new PdfiumWorkerLimit(1), _answerTimeLimit, new PdfiumWorkerCommand(SHELL, ["-c", "exit 7"]));
        byte[] encrypted = PdfFixtures.Encrypted(PdfFixtures.Plain("Locked page text"), "user-pw-1", "owner-pw-1");

        FileUnlockResult result = await unlocker.TryUnlockAsync(encrypted, EncryptedContainer.Pdf, "user-pw-1", TestContext.Current.CancellationToken);

        Assert.Equal(FileProtectionStatus.UnsupportedProtection, result.Status);
    }

    [Fact]
    public async Task Open_PastTheWorkerLimit_WaitsForAFreeWorker_AndStartsWhenADocumentIsDisposed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var workers = new PdfiumWorkerLimit(2);
        var format = new PdfiumPdfFormat(workers);
        IPdfDocument first = Open(format);
        using IPdfDocument second = Open(format);
        using Process firstWorker = Process.GetProcessById(((PdfiumPdfDocument)first).WorkerProcessId);

        Task<IPdfDocument> third = Task.Run(() => Open(format), cancellationToken);
        await Eventually.UntilAsync(() => workers.Waiting == 1, "the third document to wait for a worker", cancellationToken: cancellationToken);

        Assert.False(third.IsCompleted);
        Assert.Equal(2, workers.Live);
        first.Dispose();
        using IPdfDocument opened = await third.WaitAsync(_answerTimeLimit, cancellationToken);

        Assert.True(firstWorker.HasExited);
        Assert.Equal(2, workers.Live);
        Assert.Contains("Charlie page", opened.Text(3), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unlock_WhileTheOnlyWorkerReadsADocument_WaitsForThatWorker()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var workers = new PdfiumWorkerLimit(1);
        IPdfDocument open = Open(new PdfiumPdfFormat(workers));
        byte[] encrypted = PdfFixtures.Encrypted(PdfFixtures.Plain("Locked page text"), "user-pw-1", "owner-pw-1");

        Task<FileUnlockResult> unlocking = new OpenDocumentUnlocker(workers).TryUnlockAsync(encrypted, EncryptedContainer.Pdf, "user-pw-1", cancellationToken);
        await Eventually.UntilAsync(() => workers.Waiting == 1, "the unlock to wait for the worker", cancellationToken: cancellationToken);
        open.Dispose();

        Assert.Equal(FileProtectionStatus.Decrypted, (await unlocking.WaitAsync(_answerTimeLimit, cancellationToken)).Status);
        Assert.Equal(0, workers.Live);
    }

    private static PdfiumPdfFormat FakeWorker(string script)
    {
        return new PdfiumPdfFormat(new PdfiumWorkerLimit(1), _answerTimeLimit, new PdfiumWorkerCommand(SHELL, ["-c", script]));
    }

    private static IPdfDocument Open(PdfiumPdfFormat format)
    {
        return format.Open(ContractFixture.Stream(ContractFixture.THREE_PAGES_PDF), ContractFixture.NameOf(ContractFixture.THREE_PAGES_PDF));
    }

    private static void SkipWithoutAShell()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "These cases start a POSIX shell as a stand-in worker.");
    }
}
