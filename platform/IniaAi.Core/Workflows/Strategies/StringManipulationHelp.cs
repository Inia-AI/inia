using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Strategies;

/// <summary>
/// User-facing reference for every operation the StringManipulation block supports.
/// The block UI reads this list to drive both the operation dropdown and the in-block help panel,
/// so each supported <see cref="StringManipulationOperation"/> value MUST have a matching section here.
/// Order of this list = order in the dropdown. Keep the most-common-for-prompt-engineering at the top.
/// </summary>
public static class StringManipulationHelp
{
    public sealed record Example(string Before, string After, string Caption);

    public sealed record Section(
        StringManipulationOperation Operation,
        string Title,
        string Description,
        IReadOnlyList<string> Parameters,
        IReadOnlyList<Example> Examples);

    public static IReadOnlyList<Section> Sections { get; } =
    [
        new Section(
            StringManipulationOperation.Replace,
            "Find and replace",
            "Replaces every occurrence of some text with something else. "
                + $"To match or insert a real line break or tab, type the token {StringManipulationStrategy.TOKEN_NEWLINE} or {StringManipulationStrategy.TOKEN_TAB} in the field — see the Insert buttons below each field.",
            Parameters: ["Find", "Replace With", "Case Sensitive"],
            Examples:
            [
                new Example("hello world", "hello there", "Find='world', Replace With='there'"),
                new Example("line1\nline2", "line1 / line2", $"Find='{StringManipulationStrategy.TOKEN_NEWLINE}', Replace With=' / '"),
            ]),

        new Section(
            StringManipulationOperation.EscapeSpecialCharacters,
            "Turn line breaks into \\n",
            "Turns real line breaks and tabs into the visible escape codes '\\n' and '\\t'. "
                + "Use this before pasting a block's output into another prompt. "
                + "JSON-shaped fields (Web API Call payload, Merge JSON inputs, JSON to Excel input, Chart data) "
                + "escape substituted values automatically, so this step is no longer needed there — "
                + "already-escaped values pass through unchanged either way.",
            Parameters:
            [
                "Line breaks \\n",
                "Tabs \\t",
                "Carriage returns \\r",
                "Backslashes \\\\",
                "Double quotes \\\"",
            ],
            Examples:
            [
                new Example("first line\nsecond line", "first line\\nsecond line", "Line breaks on (default)"),
                new Example("col1\tcol2", "col1\\tcol2", "Tabs on (default)"),
            ]),

        new Section(
            StringManipulationOperation.UnescapeSpecialCharacters,
            "Turn \\n back into line breaks",
            "The reverse. Turns the visible codes '\\n' and '\\t' back into real line breaks and tabs. "
                + "Use this when an AI replies with literal '\\n' in its answer and you want real line breaks.",
            Parameters:
            [
                "Line breaks \\n",
                "Tabs \\t",
                "Carriage returns \\r",
                "Backslashes \\\\",
                "Double quotes \\\"",
            ],
            Examples:
            [
                new Example("first line\\nsecond line", "first line\nsecond line", "Line breaks on (default)"),
                new Example("a\\xb", "a\\xb", "Anything it doesn't recognise is left alone"),
            ]),

        new Section(
            StringManipulationOperation.Trim,
            "Trim spaces",
            "Removes leading and/or trailing spaces (or any characters you specify).",
            Parameters: ["Side (Both / Start / End)", "Trim Characters"],
            Examples:
            [
                new Example("  hello  ", "hello", "Side=Both"),
                new Example("  hello  ", "hello  ", "Side=Start"),
                new Example("---hello---", "hello", "Trim Characters='-'"),
            ]),

        new Section(
            StringManipulationOperation.ChangeCase,
            "Change case",
            "Converts the input to UPPERCASE, lowercase, Title Case, or Sentence case.",
            Parameters: ["Case Type"],
            Examples:
            [
                new Example("Hello World", "HELLO WORLD", "Case Type=Upper"),
                new Example("the quick fox", "The Quick Fox", "Case Type=Title"),
                new Example("hello WORLD. how Are You?", "Hello world. how are you?", "Case Type=Sentence"),
            ]),

        new Section(
            StringManipulationOperation.Length,
            "Count characters",
            "Returns how many characters are in the input, as a number.",
            Parameters: [],
            Examples:
            [
                new Example("abcde", "5", ""),
                new Example("", "0", "Empty input still produces '0'"),
            ]),

        new Section(
            StringManipulationOperation.Substring,
            "Take part of text",
            "Keeps only a slice of the input, starting at a given position for a given length. "
                + "Positions are 0-based. Leaving Length blank takes everything from the start position to the end.",
            Parameters: ["Start Index", "Length"],
            Examples:
            [
                new Example("abcdef", "cde", "Start=2, Length=3"),
                new Example("abcdef", "cdef", "Start=2, Length=(blank)"),
                new Example("abc", "", "Start=10 is past the end, returns empty"),
            ]),

        new Section(
            StringManipulationOperation.Split,
            "Split into pieces",
            "Cuts the input into pieces around a separator. Pick one piece by index (0-based; negative counts from the end). "
                + "Leave the index blank to get a JSON list of all pieces (feed it into 'Combine into one string' or a loop).",
            Parameters: ["Separator", "Split Index"],
            Examples:
            [
                new Example("a,b,c", "b", "Separator=',', Index=1"),
                new Example("a,b,c", "[\"a\",\"b\",\"c\"]", "Separator=',', Index=(blank) returns a JSON list"),
                new Example("line1\nline2", "line2", $"Separator='{StringManipulationStrategy.TOKEN_NEWLINE}', Index=-1"),
            ]),

        new Section(
            StringManipulationOperation.Join,
            "Combine into one string",
            "The reverse of 'Split into pieces'. Takes a JSON list of strings and sticks them together with a separator between each.",
            Parameters: ["Separator"],
            Examples:
            [
                new Example("[\"a\",\"b\",\"c\"]", "a - b - c", "Separator=' - '"),
                new Example("[\"line1\",\"line2\"]", "line1\nline2", $"Separator='{StringManipulationStrategy.TOKEN_NEWLINE}'"),
            ]),

        new Section(
            StringManipulationOperation.Distinct,
            "Keep unique lines",
            "Removes duplicate lines from the input. Treats the input as one item per line, trims whitespace from each line, drops blank lines, and keeps only the first occurrence of each unique line. "
                + "For natural-language lists (e.g. names, topics) where case usually doesn't matter, uncheck Case Sensitive.",
            Parameters: ["Case Sensitive"],
            Examples:
            [
                new Example("apple\nbanana\napple", "apple\nbanana", "Duplicate 'apple' is removed"),
                new Example("  apple  \nApple\napple", "apple\nApple", "Case Sensitive=on (default); whitespace is trimmed"),
                new Example("Apple\nAPPLE\napple", "Apple", "Case Sensitive=off — first occurrence wins"),
            ]),

        new Section(
            StringManipulationOperation.ReplaceUsingPattern,
            "Find and replace using a pattern (advanced)",
            "Same as 'Find and replace', but the Find field is a regular expression (regex). "
                + "Use $1, $2, ... in Replace With to reference captured groups. Only pick this if you know what a regex is.",
            Parameters: ["Find (pattern)", "Replace With", "Case Sensitive"],
            Examples:
            [
                new Example("price: 1250.00 USD", "price: 1250 dollars 00 cents USD", @"Find='(\d+)\.(\d{2})', Replace With='$1 dollars $2 cents'"),
                new Example("$1,250.00", "1250.00", @"Find='\$|,', Replace With='' removes both dollar signs and commas"),
            ]),
    ];
}
