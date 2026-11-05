using System.Text;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Fluent helper for emitting the small subset of markdown that
/// <see cref="WorkflowTextExportService"/> produces. Centralises the noisy
/// patterns (StringBuilder.AppendLine, ternary bool-to-string, code-fence
/// wrapping) so the call sites read as declarative shape instead of plumbing.
/// </summary>
internal sealed class MarkdownBuilder
{
    private const string CODE_FENCE = "```";

    private readonly StringBuilder _sb = new();

    public MarkdownBuilder H1(string text)
    {
        _ = _sb.Append("# ").AppendLine(text);
        return this;
    }

    public MarkdownBuilder H2(string text)
    {
        _ = _sb.Append("## ").AppendLine(text);
        return this;
    }

    public MarkdownBuilder H3(string text)
    {
        _ = _sb.Append("### ").AppendLine(text);
        return this;
    }

    public MarkdownBuilder H4(string text)
    {
        _ = _sb.Append("#### ").AppendLine(text);
        return this;
    }

    public MarkdownBuilder Line(string text)
    {
        _ = _sb.AppendLine(text);
        return this;
    }

    public MarkdownBuilder LineIf(bool condition, string text)
    {
        if (condition)
        {
            _ = _sb.AppendLine(text);
        }

        return this;
    }

    public MarkdownBuilder Blank()
    {
        _ = _sb.AppendLine();
        return this;
    }

    public MarkdownBuilder Bullet(string label, string value)
    {
        _ = _sb.AppendLine($"- **{label}:** {value}");
        return this;
    }

    public MarkdownBuilder Bullet(string label, int value)
    {
        _ = _sb.AppendLine($"- **{label}:** {value}");
        return this;
    }

    public MarkdownBuilder Bullet(string label, bool value)
    {
        _ = _sb.AppendLine($"- **{label}:** {(value ? "true" : "false")}");
        return this;
    }

    public MarkdownBuilder Bullet(string label, Enum value)
    {
        _ = _sb.AppendLine($"- **{label}:** {value}");
        return this;
    }

    public MarkdownBuilder BulletCode(string label, string value)
    {
        _ = _sb.AppendLine($"- **{label}:** `{value}`");
        return this;
    }

    public MarkdownBuilder BulletIf(bool condition, string label, string value)
    {
        if (condition)
        {
            _ = _sb.AppendLine($"- **{label}:** {value}");
        }

        return this;
    }

    public MarkdownBuilder BulletIf(bool condition, string label, int value)
    {
        if (condition)
        {
            _ = _sb.AppendLine($"- **{label}:** {value}");
        }

        return this;
    }

    public MarkdownBuilder BulletCodeIfSet(string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _ = _sb.AppendLine($"- **{label}:** `{value}`");
        }

        return this;
    }

    public MarkdownBuilder BulletIfSet(string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _ = _sb.AppendLine($"- **{label}:** {value}");
        }

        return this;
    }

    public MarkdownBuilder Fenced(string content)
    {
        _ = _sb.AppendLine(CODE_FENCE).AppendLine(content).AppendLine(CODE_FENCE);
        return this;
    }

    public MarkdownBuilder FencedSection(string heading, string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return this;
        }

        return H3(heading).Fenced(content).Blank();
    }

    public MarkdownBuilder FencedSectionH4(string heading, string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return this;
        }

        return H4(heading).Fenced(content).Blank();
    }

    public override string ToString()
    {
        return _sb.ToString();
    }
}
