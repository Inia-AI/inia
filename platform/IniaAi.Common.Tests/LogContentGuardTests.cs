using IniaAi.Testing;
using System.Text.RegularExpressions;

namespace IniaAi.Common.Tests;

/// <summary>
/// A log line carries ids, counts, lengths and timings, never a prompt, a model reply, the text of a
/// document or report content: the logs reach operators without the customer's permission. A
/// placeholder named for content is refused unless its last word names a size or an identity, as
/// {Length} or {PageNumber} do. A line that must log such a value says why in a
/// "// Logs on purpose: reason" comment on the call's first line or the line above it.
/// </summary>
public partial class LogContentGuardTests
{
    private const string RAZOR_PATTERN = "*.razor";
    private const string MIGRATIONS_PROJECT_PREFIX = "IniaAi.Migrations.";

    private static readonly HashSet<string> _contentWords =
    [
        "prompt", "prompts", "response", "responses", "reply", "replies", "answer", "answers",
        "overview", "overviews", "transcription", "payload", "json", "csv", "formula", "content",
        "text", "markdown", "body", "sample", "excerpt", "preview", "topics", "toc", "term", "html",
        "snippet", "chunk", "debug", "input",
    ];

    private static readonly HashSet<string> _sizeOrIdentityWords =
    [
        "length", "count", "bytes", "tokens", "ms", "size", "id", "ids", "number", "index", "key",
        "name", "names", "tag", "type", "kind", "mode", "code", "status", "version", "format",
        "hash", "model", "headers",
    ];

    private static readonly HashSet<string> _flagWords = ["has", "is", "uses", "should", "can"];

    [Fact]
    public void ProductCode_NeverLogsContent()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> offenders = [.. SourceFiles.Product(repoRoot)
            .Where(file => !Path.GetRelativePath(repoRoot, file).StartsWith(MIGRATIONS_PROJECT_PREFIX, StringComparison.Ordinal))
            .Concat(SourceFiles.Product(repoRoot, RAZOR_PATTERN))
            .SelectMany(file => Offenders(repoRoot, file))];

        Assert.True(offenders.Count == 0, "Log the size or the id instead of the content:\n  " + string.Join("\n  ", offenders));
    }

    [Theory]
    [InlineData("Response", true)]
    [InlineData("segmentsUserPrompt", true)]
    [InlineData("TopicsResponse", true)]
    [InlineData("Csv", true)]
    [InlineData("Input", true)]
    [InlineData("InputTokens", false)]
    [InlineData("Length", false)]
    [InlineData("ResponseLength", false)]
    [InlineData("PageNumber", false)]
    [InlineData("ContentType", false)]
    [InlineData("HasJson", false)]
    [InlineData("ErrorMessage", false)]
    [InlineData("ToCurrency", false)]
    public void APlaceholder_IsContent_WhenAWordNamesContent_AndTheLastWordNamesNoSizeOrIdentity(string placeholder, bool isContent)
    {
        Assert.Equal(isContent, NamesContent(placeholder));
    }

    private static IEnumerable<string> Offenders(string repoRoot, string file)
    {
        string source = File.ReadAllText(file);
        string[] lines = source.Split('\n');

        foreach (Match call in LogCall().Matches(source))
        {
            int lineIndex = source.AsSpan(0, call.Index).Count('\n');
            if (OnPurpose().IsMatch(lines[lineIndex]) || (lineIndex > 0 && OnPurpose().IsMatch(lines[lineIndex - 1])))
            {
                continue;
            }

            string template = string.Concat(StringLiteral().Matches(Arguments(source, call.Index + call.Length - 1)).Select(literal => literal.Groups["text"].Value));
            foreach (string placeholder in Placeholder().Matches(template).Select(match => match.Groups["name"].Value).Where(NamesContent))
            {
                yield return $"{Path.GetRelativePath(repoRoot, file)}:{lineIndex + 1}: {{{placeholder}}}";
            }
        }
    }

    private static bool NamesContent(string placeholder)
    {
        string[] words = [.. Word().Matches(placeholder).Select(word => word.Value.ToLowerInvariant())];

        return words.Length > 0
            && !_flagWords.Contains(words[0])
            && words.Any(_contentWords.Contains)
            && !_sizeOrIdentityWords.Contains(words[^1]);
    }

    /// <summary>The text of a call from its opening parenthesis to the one that closes it, skipping parentheses inside strings.</summary>
    private static string Arguments(string source, int openParenthesis)
    {
        int depth = 0;
        bool inString = false;

        for (int index = openParenthesis; index < source.Length; index++)
        {
            char character = source[index];
            if (inString)
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == '"')
                {
                    inString = false;
                }
            }
            else if (character == '"')
            {
                inString = true;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0)
            {
                return source[openParenthesis..(index + 1)];
            }
        }

        return source[openParenthesis..];
    }

    [GeneratedRegex(@"\.Log(?:Trace|Debug|Information|Warning|Error|Critical)?\(")]
    private static partial Regex LogCall();

    [GeneratedRegex(@"""(?<text>(?:[^""\\]|\\.)*)""")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"(?<!\{)\{@?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}(?!\})")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")]
    private static partial Regex Word();

    [GeneratedRegex(@"//\s*Logs on purpose:\s*\S")]
    private static partial Regex OnPurpose();
}
