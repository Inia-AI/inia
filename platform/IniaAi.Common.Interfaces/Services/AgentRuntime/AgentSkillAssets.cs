using System.Text.RegularExpressions;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The runtime's admission rules for skill assets, mirrored so both the save-time guard and the
/// start-time client refuse what the runtime would 400 — before a save is accepted and before
/// any bytes are uploaded. These caps are not discoverable over <c>GET /v1/limits</c> (it
/// serves only the fuses), so truth lives in the runtime's published wire contract:
/// agent-runtime <c>src/contracts/http-wire.constraints.json</c>
/// (<c>caps.maxSkillAssetsPerSkill</c>) and the staged-input name rule in
/// <c>src/run-documents.ts</c> (<c>isStagedInputName</c>).
/// </summary>
public static partial class AgentSkillAssets
{
    public const int MAX_ASSETS_PER_SKILL = 16;

    public const string NAME_RULE = "^[A-Za-z0-9._ -]{1,200}$";

    private static readonly string[] _reservedNames = ["manifest.json", ".", ".."];

    public enum ProblemKind
    {
        CountOverCap,
        NameShape,
        NameCollision,
    }

    public sealed record Problem(ProblemKind Kind, string Message);

    public static bool IsValidName(string name)
    {
        return NamePattern().IsMatch(name) && !_reservedNames.Contains(name, StringComparer.Ordinal);
    }

    /// <summary>
    /// First problem with one skill's asset list, or null when admissible.
    /// </summary>
    public static Problem? DescribeFirstProblem(string skillName, IEnumerable<(string Name, string Content)> assets)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;

        foreach ((string name, string _) in assets)
        {
            count++;
            if (count > MAX_ASSETS_PER_SKILL)
            {
                return new Problem(
                    ProblemKind.CountOverCap,
                    $"Skill '{skillName}' ships more than {MAX_ASSETS_PER_SKILL} assets, "
                    + "over the runtime's per-skill cap.");
            }

            if (!IsValidName(name))
            {
                return new Problem(
                    ProblemKind.NameShape,
                    $"Skill '{skillName}' has an asset named '{name}'. An asset name must be an "
                    + $"ordinary file name matching {NAME_RULE}, and not a reserved name.");
            }

            if (!seenNames.Add(name))
            {
                return new Problem(
                    ProblemKind.NameCollision,
                    $"Skill '{skillName}' names two assets '{name}'. Asset names must be unique "
                    + "within their skill.");
            }
        }

        return null;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9._ -]{1,200}\z")]
    private static partial Regex NamePattern();
}
