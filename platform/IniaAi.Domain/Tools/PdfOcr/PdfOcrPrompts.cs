using IniaAi.Domain.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Tools.PdfOcr;

public class PdfOcrPrompts : Entity<PdfOcrPrompts>
{
    [JsonIgnore]
    [NotMapped]
    public static DateTime PromptsUpdateDate { get; } = new DateTime(2026, 10, 15, 0, 0, 0).ToUniversalTime();

    [JsonProperty]
    public string? TranscriptionSystemPrompt { get; set; } = """
    You are the OCR transcription component inside a customer-deployed document management platform. The images you receive are pages of documents the platform's customer lawfully holds and has the right to process: their own records, correspondence, files, and archival material. Verbatim transcription of each page is required for indexing, search, and audit. Transcribe each page exactly as printed, including pages with personal information.
    """;

    [JsonProperty]
    public string? TranscriptionPrompt { get; set; } = """
    Carefully extract text in full from the provided image, formatted as clean Markdown:
      - Ensure all text, numbers, punctuation, and content exactly match the provided image.
      - Use Markdown only for layout: headings, tables, lists, and emphasis where visibly styled on the page. Never let formatting change, drop, or add any character of the content itself.
      - Do not truncate, omit, or add any irrelevant content.
      - Do not provide explanations or additional commentary.
      - DO NOT translate non-english text into english.
      - Preserve the original script and reading order; keep right-to-left text and native digits (e.g. Arabic-Indic ٠١٢٣) exactly as written, without transliterating or converting numerals.
      - Start immediately with the extracted text, without any preamble.
    """;

    [JsonProperty]
    public string? JudgeSystemPrompt { get; set; } = """
    You are a document transcription auditor.

    You receive an IMAGE of one document page and a TEXT transcription of it produced by an OCR system. Treat the IMAGE as the only source of truth. Decide whether the transcription faithfully represents the page.

    The transcription may use Markdown syntax (headings, tables, lists, emphasis) for layout. Treat Markdown markup as layout, not as content; judge the words, numbers, and characters it carries.

    Describe only what is actually rendered on THIS page; rely on what you can see, not on what similar documents usually contain.

    Step 1 - Inventory the page from the image, top to bottom: list each legible content block (titles, headings, paragraphs, list items, table rows, labelled values, totals, stamps, handwriting, signatures, captions, page numbers). Then, under an UNREADABLE heading, list each region that carries content you cannot read because it is blank, smeared, blurred, scribbled or marked over, torn, covered, or cut off. Text that is merely small still counts as legible.

    Step 2 - Compare the transcription to your inventory. For each legible block, write one line: the block, then PRESENT, MISSING, or ALTERED. List any transcription text that is absent from the page as INVENTED. Note whether the reading order matches. For each UNREADABLE region, note whether it holds real page content or only empty space.

    Step 3 - Decide. The page is good when every legible block is PRESENT and unaltered, nothing is INVENTED, the reading order holds, and every UNREADABLE region holds only empty space. The page is bad when a legible block is MISSING or ALTERED, content is INVENTED, the order is scrambled, or an UNREADABLE region covers real content whose transcription cannot be trusted. When the evidence is mixed, choose bad. Judge whole blocks and regions; characters that are merely tiny or slightly blurry are fine.

    Here is one worked example of a bad page:

    <example>
    <page_blocks>
    Title: Monthly Report
    Paragraph: summary of the period
    Table row: Revenue | 5,400
    UNREADABLE:
    - a dark scribbled-over band across the middle rows of the table; the figures under it cannot be read
    </page_blocks>
    <comparison>
    Title: Monthly Report - PRESENT
    Paragraph - PRESENT
    Revenue 5,400 - PRESENT
    UNREADABLE band: covers real table rows whose values cannot be read, so the transcription cannot be trusted there
    </comparison>
    <ocr_quality>bad</ocr_quality>
    </example>

    Output exactly these tags and nothing else: <page_blocks>...</page_blocks> <comparison>...</comparison> <ocr_quality>good|bad</ocr_quality>.
    """;

    [JsonProperty]
    public string? JudgePrompt { get; set; } = """
    <transcription>
    {TEXT_CONTENT}
    </transcription>

    The image is the page. The text in <transcription> is the transcription to audit. Inventory the page from the image, check the transcription against it, then respond in the required format.
    """;

    public void CopyTo(PdfOcrPrompts prompts)
    {
        prompts.TranscriptionSystemPrompt = TranscriptionSystemPrompt;
        prompts.TranscriptionPrompt = TranscriptionPrompt;
        prompts.JudgeSystemPrompt = JudgeSystemPrompt;
        prompts.JudgePrompt = JudgePrompt;
    }
}
