using System.Text;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Pdf.Pdfium;
using Answer = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Answer;
using Request = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Request;
using UnlockOutcome = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.UnlockOutcome;

namespace IniaAi.FileFormats.Unlock;

/// <summary>
/// The open build's unlocker. PDFium removes a PDF's security in a worker process. <see cref="OfficePackageDecryption"/>
/// decrypts an Office package. An encrypted legacy workbook has no free reader, so it gets the unsupported-protection
/// outcome.
/// </summary>
public sealed class OpenDocumentUnlocker : IDocumentUnlocker
{
    private const string PDF_NAME = "an encrypted PDF";
    private const int NO_PASSWORD = -1;
    private static readonly byte[] _zipSignature = "PK"u8.ToArray();

    private readonly TimeSpan _answerTimeLimit;
    private readonly PdfiumWorkerCommand? _command;
    private readonly PdfiumWorkerLimit _workers;

    public OpenDocumentUnlocker()
        : this(new PdfiumWorkerLimit())
    {
    }

    public OpenDocumentUnlocker(PdfiumWorkerLimit workers)
        : this(workers, PdfiumWorker.DefaultAnswerTimeLimit, command: null)
    {
    }

    internal OpenDocumentUnlocker(PdfiumWorkerLimit workers, TimeSpan answerTimeLimit, PdfiumWorkerCommand? command)
    {
        _workers = workers;
        _answerTimeLimit = answerTimeLimit;
        _command = command;
    }

    public Task<FileUnlockResult> TryUnlockAsync(
        byte[] fileBytes,
        EncryptedContainer container,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return container switch
        {
            EncryptedContainer.Pdf => Task.Run(() => UnlockPdf(fileBytes, password), cancellationToken),
            EncryptedContainer.OfficePackage => Task.Run(() => UnlockOfficePackage(fileBytes, password), cancellationToken),
            EncryptedContainer.LegacyWorkbook => Task.FromResult(FileUnlockResult.Locked(FileProtectionStatus.UnsupportedProtection)),
            _ => throw new ArgumentOutOfRangeException(nameof(container), container, "Unknown encrypted container."),
        };
    }

    /// <summary>A worker that fails, crashes or hangs on the file leaves it locked, as the paid unlocker leaves a file Aspose fails on.</summary>
    private FileUnlockResult UnlockPdf(byte[] fileBytes, string? password)
    {
        try
        {
            using PdfiumWorker worker = PdfiumWorker.Start(PDF_NAME, _workers, _answerTimeLimit, _command);
            byte[] passwordBytes = password is null ? [] : Encoding.UTF8.GetBytes(password);
            (Answer answer, BinaryReader body) = worker.Ask(Request.Unlock, sizeof(int) + passwordBytes.Length + fileBytes.Length, input =>
            {
                using var writer = new BinaryWriter(input, Encoding.UTF8, leaveOpen: true);
                writer.Write(password is null ? NO_PASSWORD : passwordBytes.Length);
                writer.Write(passwordBytes);
                writer.Write(fileBytes);
            });

            using (body)
            {
                if (answer != Answer.Done)
                {
                    return FileUnlockResult.Locked(FileProtectionStatus.UnsupportedProtection);
                }

                var outcome = (UnlockOutcome)body.ReadByte();
                bool signed = body.ReadBoolean();
                byte[] copy = PdfiumWorker.LengthPrefixedBytes(body).ToArray();
                return outcome switch
                {
                    UnlockOutcome.NotEncrypted => FileUnlockResult.NotEncrypted(),
                    UnlockOutcome.Decrypted => new FileUnlockResult(FileProtectionStatus.Decrypted, copy, WasPasswordProtected: true, signed),
                    UnlockOutcome.PasswordRequired => FileUnlockResult.Locked(FileProtectionStatus.PasswordRequired),
                    UnlockOutcome.PasswordIncorrect => FileUnlockResult.Locked(FileProtectionStatus.PasswordIncorrect),
                    _ => FileUnlockResult.Locked(FileProtectionStatus.UnsupportedProtection),
                };
            }
        }
        catch (PdfiumWorkerException)
        {
            return FileUnlockResult.Locked(FileProtectionStatus.UnsupportedProtection);
        }
    }

    private static FileUnlockResult UnlockOfficePackage(byte[] fileBytes, string? password)
    {
        try
        {
            OfficePackageDecryption decryption = OfficePackageDecryption.Read(fileBytes);
            if (string.IsNullOrEmpty(password))
            {
                return FileUnlockResult.Locked(FileProtectionStatus.PasswordRequired);
            }

            byte[]? package = decryption.Decrypt(password);
            if (package is null)
            {
                return FileUnlockResult.Locked(FileProtectionStatus.PasswordIncorrect);
            }

            return package.AsSpan().StartsWith(_zipSignature)
                ? new FileUnlockResult(FileProtectionStatus.Decrypted, package, WasPasswordProtected: true, ContainedSignature: false)
                : FileUnlockResult.Locked(FileProtectionStatus.UnsupportedProtection);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException or IOException or FormatException or OverflowException
            or System.Xml.XmlException or System.Security.Cryptography.CryptographicException or ArgumentException or OpenMcdf.FileFormatException)
        {
            return FileUnlockResult.Locked(FileProtectionStatus.UnsupportedProtection);
        }
    }
}
