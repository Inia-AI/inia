namespace IniaAi.FileFormats.ContractTests;

/// <summary>
/// The fixture files of the contract tests, written by <c>tools/synthetic-fixtures</c>
/// (<c>TemplateContractFixtures</c> and <c>CreditReviewTemplate</c>).
/// </summary>
internal static class TemplateFixtures
{
    public const string INPUTS_SHEET = "Inputs";
    public const string MODEL_SHEET = "Model";
    public const string HIDDEN_SHEET = "Hidden";
    public const string VERY_HIDDEN_SHEET = "VeryHidden";

    public const string CREDIT_REVIEW_COMPANY = "Harrowgate Aerostructures plc";
    public const string CREDIT_REVIEW_LOGS_MARKER = "Income Statement Page Numbers";
    public const int CREDIT_REVIEW_WORD_PAGES = 4;

    public static byte[] EditableWorkbook => ContractFixture.Bytes(ContractFixture.EDITABLE_WORKBOOK);

    public static byte[] FillableDocument => ContractFixture.Bytes(ContractFixture.FILLABLE_WORD_DOCUMENT);

    public static byte[] FillableMacroDocument => ContractFixture.Bytes(ContractFixture.FILLABLE_MACRO_WORD_DOCUMENT);

    /// <summary>A macro-enabled workbook with four WordPage sheets, a hidden Inputs sheet and a Logs sheet.</summary>
    public static byte[] CreditReviewTemplate => ContractFixture.Bytes(ContractFixture.MACRO_WORKBOOK);

    public static byte[] NotADocument => [0x4E, 0x6F, 0x74, 0x20, 0x61, 0x20, 0x66, 0x69, 0x6C, 0x65];
}
