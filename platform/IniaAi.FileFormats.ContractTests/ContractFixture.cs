namespace IniaAi.FileFormats.ContractTests;

/// <summary>The committed fixture files, which the build copies next to the test project that runs the contracts.</summary>
public static class ContractFixture
{
    public const string THREE_PAGES_PDF = "Pdf/three-pages.pdf";
    public const string TRUNCATED_PDF = "Pdf/truncated-cross-reference.pdf";
    public const string ROTATED_AND_CROPPED_PDF = "Pdf/rotated-and-cropped.pdf";
    public const string FACING_PAGES_PDF = "Pdf/facing-pages.pdf";
    public const string IMAGE_ONLY_PDF = "Pdf/image-only.pdf";
    public const string PROTECTED_PDF = "Pdf/encrypted.pdf";
    public const string PROTECTED_PDF_AES256 = "Pdf/encrypted-aes256.pdf";
    public const string OWNER_RESTRICTED_PDF = "Pdf/owner-restricted.pdf";
    public const string SIGNED_PDF = "Pdf/signed-encrypted.pdf";
    public const string UNKNOWN_HANDLER_PDF = "Pdf/unknown-security-handler.pdf";
    public const string NAMES_ENCRYPT_PDF = "Pdf/mentions-encrypt.pdf";
    public const string CELL_TEXTS_WORKBOOK = "Excel/cell-texts.xlsx";
    public const string MACRO_WORKBOOK = "Excel/credit-review-template.xlsm";
    public const string EDITABLE_WORKBOOK = "Templates/editable-workbook.xlsx";
    public const string PRINT_SCALED_WORKBOOK = "Excel/print-scaled.xlsx";
    public const string NO_VISIBLE_SHEET_WORKBOOK = "Excel/no-visible-sheet.xlsx";
    public const string FILLABLE_WORD_DOCUMENT = "Templates/fillable-document.docx";
    public const string FILLABLE_MACRO_WORD_DOCUMENT = "Templates/fillable-document.docm";
    public const string PROTECTED_WORKBOOK = "Excel/encrypted.xlsx";
    public const string PROTECTED_WORKBOOK_AGILE = "Excel/encrypted-agile.xlsx";
    public const string RIGHTS_MANAGED_PACKAGE = "Excel/rights-managed.xlsx";
    public const string TEN_PAGES_WORD_DOCUMENT = "Word/ten-pages.docx";
    public const string PROTECTED_WORD_DOCUMENT = "Word/encrypted.docx";
    public const string PROTECTED_WORD_DOCUMENT_AGILE = "Word/encrypted-agile.docx";
    public const string PICTURE_WORD_DOCUMENT = "Word/picture-on-first-page.docx";
    public const string LONG_WORD_DOCUMENT = "Word/long-text.docx";
    public const string TEXT_ONLY_EML = "Email/body-text-only-sample.eml";
    public const string SIGNATURE_ONLY_EML = "Email/body-signature-only-sample.eml";
    public const string PASTED_TABLE_EML = "Email/body-pasted-table-sample.eml";
    public const string OUTLOOK_MESSAGE = "Email/monthly-newsletter.msg";

    private const string FOLDER = "ContractFixtures";

    public static byte[] Bytes(string relativePath)
    {
        return File.ReadAllBytes(PathOf(relativePath));
    }

    public static MemoryStream Stream(string relativePath)
    {
        return new MemoryStream(Bytes(relativePath));
    }

    public static string NameOf(string relativePath)
    {
        return Path.GetFileName(relativePath);
    }

    private static string PathOf(string relativePath)
    {
        return Path.Combine(AppContext.BaseDirectory, FOLDER, relativePath);
    }
}
