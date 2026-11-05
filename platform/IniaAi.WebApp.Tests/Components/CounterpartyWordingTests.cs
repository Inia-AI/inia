using System.Text.RegularExpressions;
using IniaAi.Testing;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// Every screen and every message a person reads says the word in <c>CounterpartyTerms</c> where it
/// used to say counterparty. The scan covers a razor file's visible text and user-facing attributes,
/// every string literal in the admin app's code, and the server's error and validation messages.
/// Identifiers, routes, error codes, <c>{Counterparty.Name}</c> placeholders and log templates
/// keep the old word.
/// </summary>
public partial class CounterpartyWordingTests
{
    private const string WEB_APP_DIRECTORY = "IniaAi.WebApp";
    private const string RAZOR_PATTERN = "*.razor";
    private const string CSHARP_PATTERN = "*.cs";
    private const string THE_WORD = "counterpart";

    [GeneratedRegex("""@\*.*?\*@|<!--.*?-->""", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex("""^\s*@code\b""", RegexOptions.Multiline)]
    private static partial Regex CodeBlockStart();

    [GeneratedRegex("""(?<![\w@.:-])(?:Label|Title|Text|Placeholder|HelperText|aria-label|title|alt|Tooltip)\s*=\s*"(?!@)([^"]*)["]""")]
    private static partial Regex UserFacingAttribute();

    // A tag ends at the first '>' outside a quoted value and outside parentheses, so the '>' of a
    // lambda inside an attribute does not end it.
    [GeneratedRegex("""<[A-Za-z/](?:"[^"]*"|\((?:[^()]|\([^()]*\))*\)|[^>"(])*>""")]
    private static partial Regex Tag();

    [GeneratedRegex("""^@(?:(?:if|else|foreach|for|while|switch|case|using|inject|page|attribute|inherits|namespace|implements|layout|typeparam|rendermode|key)\b|\{)|^(?:else|case\b|default:|\{|\})|[;{}]$""")]
    private static partial Regex CodeLine();

    [GeneratedRegex("""@[A-Za-z_][\w.]*(?:\([^)]*\))?""")]
    private static partial Regex RazorExpression();

    // The word on its own: not part of an identifier, a route, a CSS class, an error code such as
    // Counterparty.NotFound or a placeholder such as {Counterparty.Name}.
    [GeneratedRegex("""(?<![\w./{@-])counterpart(?:y|ies)(?![\w/-]|\.\w)""", RegexOptions.IgnoreCase)]
    private static partial Regex TheWordAlone();

    // A comment, a raw, verbatim or regular string literal, or a char literal, read left to right,
    // so a quote inside a comment or a '//' inside a string is read as what it is.
    [GeneratedRegex("""//[^\n]*|/\*.*?\*/|\$*"{3}.*?"{3}|(?:\$@|@\$|@)"(?:[^"]|"")*"|\$?"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)'""", RegexOptions.Singleline)]
    private static partial Regex CSharpToken();

    // A lowercase literal that is only the word is a key, a kind or a CSS class name.
    [GeneratedRegex("""^\$?@?"counterpart(?:y|ies)"$""")]
    private static partial Regex KeyLiteral();

    [GeneratedRegex("""\bLog\w*\s*\(|\.Log\s*\(|\bLoggerMessage\b|\[\s*\w*Parameter\s*\(""")]
    private static partial Regex NotReadByAPerson();

    [GeneratedRegex("""\.WithMessage\s*\(|\.WithName\s*\(|\bError\.(?:Validation|NotFound|Conflict|Failure|Forbidden|Unauthorized|Unexpected|Custom)\s*\(""")]
    private static partial Regex ServerMessage();

    [GeneratedRegex("""[\\/]Common[\\/]Errors[\\/]|ErrorFactory\.cs$""")]
    private static partial Regex ErrorFactoryFile();

    [Fact]
    public void NoScreen_SaysCounterparty()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> found = [.. WebAppFiles(repoRoot, RAZOR_PATTERN)
            .SelectMany(path => VisibleUsesOfTheWord(File.ReadAllText(path))
                .Select(text => $"{Path.GetRelativePath(repoRoot, path)}: {text}"))];

        Assert.True(found.Count == 0, $"Screen text that says counterparty instead of the CounterpartyTerms word:\n{string.Join('\n', found)}");
    }

    [Fact]
    public void NoScreenCode_SaysCounterparty()
    {
        string repoRoot = RepositoryRoot.Find();

        IEnumerable<(string Path, string Code)> code = WebAppFiles(repoRoot, CSHARP_PATTERN)
            .Select(path => (path, File.ReadAllText(path)))
            .Concat(WebAppFiles(repoRoot, RAZOR_PATTERN).Select(path => (path, CodeBlockOf(File.ReadAllText(path)))));

        List<string> found = [.. code
            .SelectMany(file => LiteralsSayingTheWord(file.Code, _ => true)
                .Select(literal => $"{Path.GetRelativePath(repoRoot, file.Path)}: {literal}"))];

        Assert.True(found.Count == 0, $"Admin app text that says counterparty instead of the CounterpartyTerms word:\n{string.Join('\n', found)}");
    }

    [Fact]
    public void NoServerMessage_SaysCounterparty()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> found = [.. ServerFiles(repoRoot)
            .SelectMany(path => ServerMessagesSayingTheWord(path, File.ReadAllText(path))
                .Select(literal => $"{Path.GetRelativePath(repoRoot, path)}: {literal}"))];

        Assert.True(found.Count == 0, $"Error or validation messages that say counterparty instead of the CounterpartyTerms word:\n{string.Join('\n', found)}");
    }

    [Fact]
    public void TheScan_CatchesTheWordInVisibleTextAndLabels()
    {
        const string MARKUP = """
            <MudText Typo="Typo.h6">Counterparty details</MudText>
            <MudSelect Label="Select Counterparty" @bind-Value="Model.Counterparty" />
            <MudTextField Placeholder="Search counterparties" />
            <span aria-label="Remove counterparty" class="dep-legend--counterparty"></span>
            """;

        List<string> found = [.. VisibleUsesOfTheWord(MARKUP)];

        Assert.Equal(4, found.Count);
        Assert.Contains(found, text => text.Contains("Counterparty details", StringComparison.Ordinal));
        Assert.Contains(found, text => text.Contains("Select Counterparty", StringComparison.Ordinal));
        Assert.Contains(found, text => text.Contains("Search counterparties", StringComparison.Ordinal));
        Assert.Contains(found, text => text.Contains("Remove counterparty", StringComparison.Ordinal));
    }

    [Fact]
    public void TheScan_LeavesIdentifiersBindingsAndRoutes()
    {
        const string MARKUP = """
            @page "/counterparties"
            @using IniaAi.Domain.Entities.Counterparties
            <MudSelect T="Counterparty" @bind-Value="Block.ShouldUpdateCounterpartyWithResult"
                       Label="@($"Select {CounterpartyTerms.SINGULAR}")"
                       ToStringFunc="@(c => c?.Name)">
                @foreach (var counterparty in Counterparties.Except(new List<Counterparty>()))
                {
                    <MudSelectItem T="Counterparty" Value="@counterparty">@counterparty.Name</MudSelectItem>
                }
            </MudSelect>
            <MudLink Href="/counterparties/edit">@CounterpartyTerms.PLURAL</MudLink>
            @code {
                private List<Counterparty> _counterparties = [];
            }
            """;

        Assert.Empty(VisibleUsesOfTheWord(MARKUP));
    }

    [Fact]
    public void TheLiteralScan_CatchesNoticesConstantsAndMessages()
    {
        const string CODE = """
            Snackbar.Add("Counterparty saved.", Severity.Success);
            private const string EMPTY = "No counterparties yet";
            string note = @"Pick a counterparty";
            return Error.NotFound("Counterparty.NotFound", $"Counterparty with ID '{id}' was not found.");
            _ = RuleFor(x => x.Id).NotEmpty().WithMessage("The counterparty ID is required.");
            """;

        List<string> server = [.. ServerMessagesSayingTheWord("Validator.cs", CODE)];
        List<string> screen = [.. LiteralsSayingTheWord(CODE, _ => true)];

        Assert.Equal(["$\"Counterparty with ID '{id}' was not found.\"", "\"The counterparty ID is required.\""], server);
        Assert.Equal(
            [
                "\"Counterparty saved.\"",
                "\"No counterparties yet\"",
                "@\"Pick a counterparty\"",
                "$\"Counterparty with ID '{id}' was not found.\"",
                "\"The counterparty ID is required.\"",
            ],
            screen);
    }

    [Fact]
    public void TheLiteralScan_LeavesRoutesCodesPlaceholdersKeysAndLogs()
    {
        const string CODE = """
            // Counterparty "comment"
            Navigation.NavigateTo("/counterparties/edit");
            public const string KIND = "counterparty";
            [CascadingParameter(Name = "Counterparties")] public List<Counterparty> Counterparties { get; set; }
            return Error.NotFound("Counterparty.NotFound", $"{CounterpartyTerms.SINGULAR} with ID '{id}' was not found.");
            string template = "Dear {Counterparty.Name},";
            _logger.LogError("No counterparties of type {Type} found. " + "Please ensure the counterparty type exists.", type);
            """;

        Assert.Empty(LiteralsSayingTheWord(CODE, _ => true));
    }

    private static IEnumerable<string> WebAppFiles(string repoRoot, string pattern)
    {
        return Directory.EnumerateFiles(Path.Combine(repoRoot, WEB_APP_DIRECTORY), pattern, SearchOption.AllDirectories);
    }

    private static IEnumerable<string> ServerFiles(string repoRoot)
    {
        return Directory.EnumerateDirectories(repoRoot, "IniaAi.*")
            .Where(project =>
            {
                string name = Path.GetFileName(project);
                return !name.Contains("Test", StringComparison.Ordinal)
                    && !name.Contains("Migrations", StringComparison.Ordinal)
                    && name != WEB_APP_DIRECTORY;
            })
            .SelectMany(project => Directory.EnumerateFiles(project, CSHARP_PATTERN, SearchOption.AllDirectories));
    }

    private static IEnumerable<string> ServerMessagesSayingTheWord(string path, string code)
    {
        bool wholeFile = ErrorFactoryFile().IsMatch(path);
        return LiteralsSayingTheWord(code, statement => wholeFile || ServerMessage().IsMatch(statement));
    }

    private static string CodeBlockOf(string razor)
    {
        Match code = CodeBlockStart().Match(razor);
        return code.Success ? razor[code.Index..] : string.Empty;
    }

    private static IEnumerable<string> VisibleUsesOfTheWord(string razor)
    {
        string markup = Comment().Replace(razor, string.Empty);
        Match code = CodeBlockStart().Match(markup);
        if (code.Success)
        {
            markup = markup[..code.Index];
        }

        foreach (Match attribute in UserFacingAttribute().Matches(markup))
        {
            if (TheWordAlone().IsMatch(attribute.Groups[1].Value))
            {
                yield return attribute.Value;
            }
        }

        foreach (string line in Tag().Replace(markup, "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || CodeLine().IsMatch(trimmed))
            {
                continue;
            }

            if (TheWordAlone().IsMatch(RazorExpression().Replace(trimmed, string.Empty)))
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>
    /// The string literals that say the word, skipping a log template, a parameter attribute's name
    /// and a key. <paramref name="inScope"/> gets the statement up to the literal and decides
    /// whether a person reads it.
    /// </summary>
    private static IEnumerable<string> LiteralsSayingTheWord(string code, Func<string, bool> inScope)
    {
        if (!code.Contains(THE_WORD, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        int statementStart = 0;
        int scanned = 0;
        foreach (Match token in CSharpToken().Matches(code))
        {
            int boundary = code.AsSpan(scanned, token.Index - scanned).LastIndexOfAny(';', '{', '}');
            if (boundary >= 0)
            {
                statementStart = scanned + boundary + 1;
            }

            scanned = token.Index + token.Length;
            string literal = token.Value;
            if (literal.StartsWith('/') || literal.StartsWith('\'') || !TheWordAlone().IsMatch(literal) || KeyLiteral().IsMatch(literal))
            {
                continue;
            }

            string statement = code[statementStart..token.Index];
            if (!NotReadByAPerson().IsMatch(statement) && inScope(statement))
            {
                yield return literal;
            }
        }
    }
}
