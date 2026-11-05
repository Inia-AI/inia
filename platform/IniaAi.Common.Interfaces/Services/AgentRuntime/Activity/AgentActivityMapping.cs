namespace IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

/// <summary>
/// The one file that owns the projection's own words: neutral machinery labels, the phase
/// vocabulary, and the retry copy. Every string here must stay domain-neutral — domain
/// flavour enters a feed only through the open noun slots (skill names, document names,
/// search terms, page numbers) and through agent-authored text, never through this
/// catalogue. <see cref="AllMappingText"/> exposes the catalogue so a test can hold that
/// line mechanically.
/// </summary>
public static class AgentActivityMapping
{
    public const string LOADED_SKILL_LABEL = "Loaded the skill";
    public const string PLANNED_THE_WORK_LABEL = "Planned the work";
    public const string CHECKED_DOCUMENT_SET_LABEL = "Checked the document set";
    public const string CONVERTED_DOCUMENTS_LABEL = "Converted the documents to searchable text";
    public const string SEARCHED_DOCUMENTS_LABEL = "Searched the documents";
    public const string READ_CONVERTED_TEXT_LABEL = "Read through the document text";
    public const string VIEWED_PAGE_LABEL = "Viewed a page with its own eyes";
    public const string PAGE_DETAIL_PREFIX = "Page";
    public const string RECORDED_WORKING_FILE_LABEL = "Recorded a working file";
    public const string WROTE_AND_RAN_CODE_LABEL = "Wrote and ran its own code";
    public const string COMPOSED_ANSWER_LABEL = "Composed the final answer";
    public const string RETRY_LABEL = "Took another look";

    public const string ANALYSING_GAP_LABEL = "Analysing…";

    public static readonly IReadOnlyList<AgentActivityPhase> Phases =
    [
        new("Getting ready", ANALYSING_GAP_LABEL),
        new("Reading the documents", ANALYSING_GAP_LABEL),
        new("Verifying visually", ANALYSING_GAP_LABEL),
        new("Recording findings", ANALYSING_GAP_LABEL),
        new("Composing the answer", ANALYSING_GAP_LABEL),
    ];

    public const int PHASE_GETTING_READY = 0;
    public const int PHASE_READING_DOCUMENTS = 1;
    public const int PHASE_VERIFYING_VISUALLY = 2;
    public const int PHASE_RECORDING_FINDINGS = 3;
    public const int PHASE_COMPOSING_ANSWER = 4;

    public static IEnumerable<string> AllMappingText()
    {
        yield return LOADED_SKILL_LABEL;
        yield return PLANNED_THE_WORK_LABEL;
        yield return CHECKED_DOCUMENT_SET_LABEL;
        yield return CONVERTED_DOCUMENTS_LABEL;
        yield return SEARCHED_DOCUMENTS_LABEL;
        yield return READ_CONVERTED_TEXT_LABEL;
        yield return VIEWED_PAGE_LABEL;
        yield return PAGE_DETAIL_PREFIX;
        yield return RECORDED_WORKING_FILE_LABEL;
        yield return WROTE_AND_RAN_CODE_LABEL;
        yield return COMPOSED_ANSWER_LABEL;
        yield return RETRY_LABEL;
        yield return ANALYSING_GAP_LABEL;
        foreach (AgentActivityPhase phase in Phases)
        {
            yield return phase.Label;
            yield return phase.GapLabel;
        }
    }
}
