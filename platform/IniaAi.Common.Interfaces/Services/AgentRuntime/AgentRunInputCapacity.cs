using System.Text;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The runtime's staged-input admission ceilings (agent-runtime <c>run-documents.ts</c> /
/// <c>config.ts</c>), mirrored so the client's pre-HTTP validation and the verification
/// capacity pre-flight read one authority. What counts toward the byte budget lives here too:
/// document content plus each skill file's content and asset bytes — the pre-flight measures
/// documents by declared size where the client measures fetched bytes, but both charge skills
/// through <see cref="SkillFileBytes"/> so the two rules cannot drift apart. Re-carried
/// continuation files charge the same budget (<see cref="AgentRunContinuationCapacity"/>).
/// Discovery and pre-flight only — the runtime's own 400 stays the enforcement.
/// <para>
/// Decided: these numbers stay a stated mirror rather than moving onto
/// <c>/v1/limits</c>. The fuse ceilings ride that route because a stale fuse silently clamps
/// paid work; a stale input ceiling only shifts where a refusal fires — the runtime's 400
/// replaces the client's — so the mirror costs nothing when wrong. The stated re-check is the
/// runtime's contract fixtures, which pin the wire behavior these constants describe.
/// </para>
/// </summary>
public static class AgentRunInputCapacity
{
    /// <summary>Per-array entry ceiling: documents and skill files are each capped at this.</summary>
    public const int MAX_DOCUMENTS_PER_RUN = 100;

    /// <summary>Decoded-byte budget shared by documents, skill files, and skill assets.</summary>
    public const long INPUT_BUDGET_BYTES = 200L * 1024 * 1024;

    /// <summary>
    /// What a pre-flight charges for a document whose size is not declared: the budget's fair
    /// share of one document slot. A stored document with no size metadata is not a free
    /// document — charging zero lets a surface of unmeasured files pass the pre-flight and then
    /// be rejected by the runtime on fetched bytes, at the full IO the pre-flight exists to
    /// avoid. The fair share bounds that error at the budget itself, never below it.
    /// </summary>
    public const long UNKNOWN_DOCUMENT_BYTES = INPUT_BUDGET_BYTES / MAX_DOCUMENTS_PER_RUN;

    /// <summary>
    /// One document's charge against the byte budget: its declared size, or
    /// <see cref="UNKNOWN_DOCUMENT_BYTES"/> when nothing declares one.
    /// </summary>
    public static long DeclaredDocumentBytes(long? declaredSizeBytes)
    {
        return declaredSizeBytes is > 0 ? declaredSizeBytes.Value : UNKNOWN_DOCUMENT_BYTES;
    }

    public static long SkillFileBytes(IEnumerable<AgentRunSkillFile> skillFiles)
    {
        return skillFiles.Sum(static skill => (long)Encoding.UTF8.GetByteCount(skill.Content)
            + skill.Assets.Sum(static asset => (long)Encoding.UTF8.GetByteCount(asset.Content)));
    }

    /// <summary>
    /// Everything a start request charges against <see cref="INPUT_BUDGET_BYTES"/>: fetched
    /// document bytes, skill bytes, and re-carried out/ bytes. The one expression of the sum,
    /// shared by the client's refusal and the dispatcher's degrade-before-HTTP, so a new
    /// byte-carrying field cannot be counted by one and missed by the other. The session
    /// bundle is deliberately absent — it has its own cap and never charges this budget.
    /// </summary>
    public static long StartRequestBytes(AgentRunStartRequest request)
    {
        return request.Documents.Sum(static document => (long)document.Content.Length)
            + SkillFileBytes(request.SkillFiles)
            + (request.Continuation?.OutFiles.Sum(static file => (long)file.Content.Length) ?? 0L);
    }
}
