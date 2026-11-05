using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IniaAi.Domain.JsonSchemas;

/// <summary>
/// The summaries of the Domain's own code comments, as plain sentences: the build writes them to
/// <c>IniaAi.Domain.xml</c> beside the assembly, and the workflow schema hands them to the editor
/// agent as what each block and setting does. Empty when the file is missing; nothing fails.
/// </summary>
public static partial class XmlDocComments
{
    private const string MEMBER = "member";
    private const string NAME = "name";
    private const string SUMMARY = "summary";
    private const string CREF = "cref";
    private const string LANGWORD = "langword";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> _summaries = new(Load);

    /// <summary>The summary of a type, as one plain paragraph; null when it has none.</summary>
    public static string? Of(Type type)
    {
        return _summaries.Value.GetValueOrDefault("T:" + Name(type));
    }

    /// <summary>The summary of a property, field or enum member, as one plain paragraph; null when it has none.</summary>
    public static string? Of(MemberInfo member)
    {
        string prefix = member switch
        {
            PropertyInfo => "P:",
            FieldInfo => "F:",
            _ => string.Empty,
        };

        return prefix.Length == 0 || member.DeclaringType is not { } owner
            ? null
            : _summaries.Value.GetValueOrDefault(prefix + Name(owner) + "." + member.Name);
    }

    private static string Name(Type type)
    {
        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static IReadOnlyDictionary<string, string> Load()
    {
        string path = Path.ChangeExtension(typeof(XmlDocComments).Assembly.Location, ".xml");
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        var summaries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement member in XDocument.Load(path).Descendants(MEMBER))
        {
            if (member.Attribute(NAME)?.Value is string name && member.Element(SUMMARY) is XElement summary
                && Plain(summary) is { Length: > 0 } text)
            {
                summaries[name] = text;
            }
        }

        return summaries;
    }

    /// <summary>A summary as it reads: references by their last name, code as written, one space between words.</summary>
    private static string Plain(XElement summary)
    {
        string text = string.Concat(summary.Nodes().Select(Text));
        return Whitespace().Replace(text, " ").Trim();
    }

    private static string Text(XNode node)
    {
        return node switch
        {
            XText text => text.Value,
            XElement { Name.LocalName: "see" or "seealso" } see when see.Attribute(CREF)?.Value is string cref => LastPart(cref),
            XElement { Name.LocalName: "see" } see when see.Attribute(LANGWORD)?.Value is string word => word,
            XElement { Name.LocalName: "paramref" or "typeparamref" } reference => reference.Attribute(NAME)?.Value ?? string.Empty,
            XElement element => string.Concat(element.Nodes().Select(Text)),
            _ => string.Empty,
        };
    }

    /// <summary><c>P:IniaAi.Domain.Foo.Bar</c> reads as <c>Bar</c>; a method's argument list is dropped.</summary>
    private static string LastPart(string cref)
    {
        string name = cref.Contains(':', StringComparison.Ordinal) ? cref[(cref.IndexOf(':', StringComparison.Ordinal) + 1)..] : cref;
        int arguments = name.IndexOf('(', StringComparison.Ordinal);
        if (arguments >= 0)
        {
            name = name[..arguments];
        }

        return name[(name.LastIndexOf('.') + 1)..];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
