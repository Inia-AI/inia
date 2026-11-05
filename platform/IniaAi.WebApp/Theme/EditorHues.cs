using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using MudBlazor;

namespace IniaAi.WebApp.Theme;

public static class EditorHues
{
    public const string LOOP = "#0EA5E9";
    public const string FALLBACK = "#94A3B8";

    /// <summary>The brand amber: marks what the workflow agent changed, and its icon.</summary>
    public const string AGENT_ACCENT = "#F4A94E";

    /// <summary>Brand navy, legible on the amber in light and dark mode alike.</summary>
    public const string AGENT_ACCENT_INK = "#1A3850";

    public static readonly IReadOnlyDictionary<string, string> CssVariables = new Dictionary<string, string>
    {
        ["--editor-loop-hue"] = LOOP,
        ["--editor-output-hue"] = Colors.Orange.Accent4,
        ["--editor-library-hue"] = "#8B5CF6",
        ["--editor-tag-accent"] = "#2DD4BF",
        ["--editor-agent-accent"] = AGENT_ACCENT,
        ["--editor-agent-accent-ink"] = AGENT_ACCENT_INK,
    };

    public static string Block(BlockTypes type) => type switch
    {
        BlockTypes.AiQuery => "#7C83FF",
        BlockTypes.IntelligentSearch => "#22D3EE",
        BlockTypes.SimpleRag => "#3B82F6",
        BlockTypes.BuildTopicsForAllDocuments => "#10B981",
        BlockTypes.CalculateNumber => "#14B8A6",
        BlockTypes.Merge => "#A855F7",
        BlockTypes.StringManipulation => "#FB923C",
        BlockTypes.DynamicChart => "#EC4899",
        BlockTypes.Json2Xlsx => "#22C55E",
        BlockTypes.ExchangeRate => "#F59E0B",
        BlockTypes.WebApiCall => "#D946EF",
        BlockTypes.SwitchCase => "#FB7185",
        BlockTypes.MergeJson => "#84CC16",
        BlockTypes.PeerFinancials => "#0D9488",
        BlockTypes.LoopStart => LOOP,
        BlockTypes.LoopEnd => "#9333EA",
        BlockTypes.Agent => "#EAB308",
        BlockTypes.Verification => "#CA8A04",
        _ => BlockTypeCatalog.Find(type)?.Hue ?? FALLBACK
    };

    public static string Group(GroupColor? color) => color switch
    {
        GroupColor.Color1 => Colors.Pink.Default,
        GroupColor.Color2 => "var(--mud-palette-text-primary)",
        GroupColor.Color3 => Colors.Purple.Default,
        GroupColor.Color4 => Colors.Indigo.Default,
        GroupColor.Color5 => Colors.LightBlue.Default,
        GroupColor.Color6 => Colors.Yellow.Default,
        GroupColor.Color7 => Colors.Green.Default,
        _ => Colors.Orange.Accent4
    };
}
