namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// The verdict contract's wire strings, shared by the envelope schema, the parser, and the
/// persisted judgment so the three can never drift. Spec §3 in
/// <c>verification-agent-v1.md</c> (agent-runtime repo).
/// </summary>
public static class VerificationOutcomes
{
    public const string PASS = "pass";
    public const string FAIL_REVISABLE = "fail-revisable";
    public const string FAIL_UNSATISFIABLE = "fail-unsatisfiable";
}

public static class VerificationMemberVerdicts
{
    public const string PASS = "pass";
    public const string FAIL = "fail";
}

/// <summary>
/// The four per-criterion states. <c>unsatisfiable</c> is the only route to the hard-stop
/// outcome and fires on first assertion; <c>cannot-assess</c> is the honest exit — not a pass,
/// not a redo trigger, no evidence allowed.
/// </summary>
public static class VerificationCriterionStates
{
    public const string MET = "met";
    public const string NOT_MET = "not-met";
    public const string CANNOT_ASSESS = "cannot-assess";
    public const string UNSATISFIABLE = "unsatisfiable";
}

public static class VerificationEvidenceKinds
{
    public const string QUOTE = "quote";
    public const string COMPUTATION = "computation";
}
