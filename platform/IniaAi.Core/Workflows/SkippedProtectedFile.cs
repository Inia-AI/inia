using System.Text.Json;

namespace IniaAi.Core.Workflows;

/// <summary>
/// A file an intake channel could not decrypt and therefore excluded from a job. Carried from
/// intake to <see cref="WorkflowHandler"/> as a job input under
/// <see cref="Common.Constants.IntakeInputKeys.SKIPPED_PROTECTED_FILES"/>, where it becomes a
/// persisted Skipped log row on the execution — the durable, operator-visible record of the gap.
/// </summary>
public sealed record SkippedProtectedFile(string FileName, string Reason)
{
    public static string Serialize(IReadOnlyList<SkippedProtectedFile> skippedFiles)
    {
        return JsonSerializer.Serialize(skippedFiles);
    }

    public static List<SkippedProtectedFile> Deserialize(string serialized)
    {
        return JsonSerializer.Deserialize<List<SkippedProtectedFile>>(serialized) ?? [];
    }
}
