using UnlockOutcome = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.UnlockOutcome;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// Removes a PDF's security: PDFium opens the file, with the password when it has an open password, and saves a copy
/// without its security handler. A file with only a permissions password needs no password.
/// </summary>
internal static class PdfiumUnlock
{
    public static (UnlockOutcome Outcome, bool Signed, byte[]? Copy) Unlock(ReadOnlyMemory<byte> file, string? password)
    {
        try
        {
            using PdfiumFile open = PdfiumFile.Load(file.Span, password: null);
            return open.IsEncrypted ? WithoutSecurity(open) : (UnlockOutcome.NotEncrypted, false, null);
        }
        catch (PdfiumLoadException exception) when (exception.ErrorCode == PdfiumNative.FPDF_ERR_PASSWORD && !string.IsNullOrEmpty(password))
        {
            return WithPassword(file, password);
        }
        catch (PdfiumLoadException exception)
        {
            return exception.ErrorCode switch
            {
                PdfiumNative.FPDF_ERR_PASSWORD => (UnlockOutcome.PasswordRequired, false, null),
                PdfiumNative.FPDF_ERR_SECURITY => (UnlockOutcome.UnsupportedProtection, false, null),
                _ => (UnlockOutcome.NotEncrypted, false, null),
            };
        }
    }

    private static (UnlockOutcome Outcome, bool Signed, byte[]? Copy) WithPassword(ReadOnlyMemory<byte> file, string password)
    {
        try
        {
            using PdfiumFile open = PdfiumFile.Load(file.Span, password);
            return WithoutSecurity(open);
        }
        catch (PdfiumLoadException exception)
        {
            return exception.ErrorCode == PdfiumNative.FPDF_ERR_PASSWORD
                ? (UnlockOutcome.PasswordIncorrect, false, null)
                : (UnlockOutcome.UnsupportedProtection, false, null);
        }
    }

    /// <summary>The copy must open without a password and carry no security handler, or the strip did not work.</summary>
    private static (UnlockOutcome Outcome, bool Signed, byte[]? Copy) WithoutSecurity(PdfiumFile open)
    {
        bool signed = open.SignatureCount > 0;
        byte[] copy = open.SaveWithoutSecurity();
        try
        {
            using PdfiumFile check = PdfiumFile.Load(copy, password: null);
            return check.IsEncrypted ? (UnlockOutcome.UnsupportedProtection, false, null) : (UnlockOutcome.Decrypted, signed, copy);
        }
        catch (PdfiumLoadException)
        {
            return (UnlockOutcome.UnsupportedProtection, false, null);
        }
    }
}
