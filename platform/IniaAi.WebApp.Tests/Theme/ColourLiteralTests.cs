using IniaAi.Testing;
using System.Text.RegularExpressions;

namespace IniaAi.WebApp.Tests.Theme;

public partial class ColourLiteralTests
{
    private const string WEB_APP_DIRECTORY = "IniaAi.WebApp";

    private static readonly string[] _scannedExtensions = [".razor", ".css", ".cs", ".js"];

    private const string THEME_DIRECTORY = "Theme/";

    private static readonly string[] _vendored =
    [
        "wwwroot/bootstrap.min.css",
        "wwwroot/qrcode.min.js",
        "wwwroot/open-iconic/",
        "wwwroot/PDFObject-2.3.0/",
        "wwwroot/js/prompt-editor.bundle.js",
        "wwwroot/js/src/node_modules/",
        "wwwroot/lib/elkjs/",
    ];

    [GeneratedRegex("""#[0-9A-Fa-f]{3}(?:[0-9A-Fa-f]|[0-9A-Fa-f]{3}(?:[0-9A-Fa-f]{2})?)?\b|\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\(|\bColors\.[A-Z]""")]
    private static partial Regex ColourLiteral();

    [GeneratedRegex("""\b(?:color|background(?:-color)?|border(?:-[a-z]+)*|outline(?:-color)?|fill|stroke|(?:box|text)-shadow|caret-color|accent-color)\s*:\s*[^;"'{}]*(?<![\w-])(?:aliceblue|antiquewhite|aqua|aquamarine|azure|beige|bisque|black|blanchedalmond|blue|blueviolet|brown|burlywood|cadetblue|chartreuse|chocolate|coral|cornflowerblue|cornsilk|crimson|cyan|darkblue|darkcyan|darkgoldenrod|darkgray|darkgreen|darkgrey|darkkhaki|darkmagenta|darkolivegreen|darkorange|darkorchid|darkred|darksalmon|darkseagreen|darkslateblue|darkslategray|darkslategrey|darkturquoise|darkviolet|deeppink|deepskyblue|dimgray|dimgrey|dodgerblue|firebrick|floralwhite|forestgreen|fuchsia|gainsboro|ghostwhite|gold|goldenrod|gray|green|greenyellow|grey|honeydew|hotpink|indianred|indigo|ivory|khaki|lavender|lavenderblush|lawngreen|lemonchiffon|lightblue|lightcoral|lightcyan|lightgoldenrodyellow|lightgray|lightgreen|lightgrey|lightpink|lightsalmon|lightseagreen|lightskyblue|lightslategray|lightslategrey|lightsteelblue|lightyellow|lime|limegreen|linen|magenta|maroon|mediumaquamarine|mediumblue|mediumorchid|mediumpurple|mediumseagreen|mediumslateblue|mediumspringgreen|mediumturquoise|mediumvioletred|midnightblue|mintcream|mistyrose|moccasin|navajowhite|navy|oldlace|olive|olivedrab|orange|orangered|orchid|palegoldenrod|palegreen|paleturquoise|palevioletred|papayawhip|peachpuff|peru|pink|plum|powderblue|purple|rebeccapurple|red|rosybrown|royalblue|saddlebrown|salmon|sandybrown|seagreen|seashell|sienna|silver|skyblue|slateblue|slategray|slategrey|snow|springgreen|steelblue|tan|teal|thistle|tomato|turquoise|violet|wheat|white|whitesmoke|yellow|yellowgreen)(?![\w-])""", RegexOptions.IgnoreCase)]
    private static partial Regex NamedColour();

    [Fact]
    public void EveryColour_ComesFromTheTheme()
    {
        string webApp = Path.Combine(RepositoryRoot.Find(), WEB_APP_DIRECTORY);

        List<string> literals = [.. Directory
            .EnumerateFiles(webApp, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(webApp, path).Replace('\\', '/'))
            .Where(IsOwnSource)
            .Where(file => !file.StartsWith(THEME_DIRECTORY, StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(Path.Combine(webApp, file))
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(entry => ColourLiteral().IsMatch(entry.Line) || NamedColour().IsMatch(entry.Line))
                .Select(entry => $"{file}:{entry.Number}: {entry.Line.Trim()}"))];

        Assert.True(literals.Count == 0, $"Colour literals outside the theme:\n{string.Join('\n', literals)}");
    }

    private static bool IsOwnSource(string file) =>
        _scannedExtensions.Any(extension => file.EndsWith(extension, StringComparison.Ordinal))
        && !_vendored.Any(vendored => file.StartsWith(vendored, StringComparison.Ordinal));
}
