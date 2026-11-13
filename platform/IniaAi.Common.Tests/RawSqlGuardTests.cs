using IniaAi.Testing;
using System.Text.RegularExpressions;

namespace IniaAi.Common.Tests;

/// <summary>
/// Completeness guard for ASVS 1.2.4 (raw database commands) and 1.5.2 (deserialization that lets
/// data choose a type). A product line that runs database command text it built by hand is refused
/// unless a "// Raw SQL reviewed: reason" comment on the line or the line above says why it is safe:
/// every request-, document- or model-controlled value enters as a parameter, and every table or
/// column name comes from the model's metadata or a constant. A new raw command cannot ship until a
/// reviewer classifies it and marks it. The interpolated calls (FromSqlInterpolated, SqlQuery and
/// ExecuteSqlInterpolated) take a FormattableString and make every value a parameter, so they need
/// no marker. No product file may use type-name deserialization at all.
/// </summary>
public partial class RawSqlGuardTests
{
    private const string MIGRATIONS_PROJECT_PREFIX = "IniaAi.Migrations.";

    [Fact]
    public void EveryRawDatabaseCommandSiteIsReviewed()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> unreviewed = [.. ProductFiles(repoRoot).SelectMany(file => UnmarkedRawSql(repoRoot, file))];

        Assert.True(
            unreviewed.Count == 0,
            "A product line runs a raw database command without a review marker. Classify every value that "
            + "enters the command (a request-, document- or model-controlled value must be a parameter, never part "
            + "of the text; a name must come from the model's metadata or a constant), then add a comment "
            + "\"// Raw SQL reviewed: <reason>\" on that line or the line above:\n  " + string.Join("\n  ", unreviewed));
    }

    [Fact]
    public void NoProductCodeUsesTypeNameDeserialization()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> offenders = [.. ProductFiles(repoRoot)
            .Where(file => TypeNameDeserialization().IsMatch(File.ReadAllText(file)))
            .Select(file => Relative(repoRoot, file))];

        Assert.True(
            offenders.Count == 0,
            "A product file uses deserialization that lets data choose a type (ASVS 1.5.2). Remove it:\n  "
            + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> UnmarkedRawSql(string repoRoot, string file)
    {
        string text = File.ReadAllText(file);
        if (!RawSqlCall().IsMatch(text))
        {
            yield break;
        }

        string[] lines = text.Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            if (!RawSqlCall().IsMatch(lines[index]))
            {
                continue;
            }

            if (ReviewMarker().IsMatch(lines[index]) || (index > 0 && ReviewMarker().IsMatch(lines[index - 1])))
            {
                continue;
            }

            yield return $"{Relative(repoRoot, file)}:{index + 1}: {lines[index].Trim()}";
        }
    }

    private static IEnumerable<string> ProductFiles(string repoRoot)
    {
        return SourceFiles.Product(repoRoot)
            .Where(file => !Relative(repoRoot, file).StartsWith(MIGRATIONS_PROJECT_PREFIX, StringComparison.Ordinal));
    }

    private static string Relative(string repoRoot, string file)
    {
        return Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
    }

    [GeneratedRegex(@"FromSqlRaw|ExecuteSqlRaw|SqlQueryRaw|CommandText\s*=|new\s+(Npgsql|Sql|Sqlite)Command\(")]
    private static partial Regex RawSqlCall();

    [GeneratedRegex(@"//\s*Raw SQL reviewed:")]
    private static partial Regex ReviewMarker();

    [GeneratedRegex(@"TypeNameHandling|BinaryFormatter|NetDataContractSerializer|LosFormatter|ObjectStateFormatter|XmlSerializer\(|JavaScriptSerializer|TypeNameAssemblyFormat")]
    private static partial Regex TypeNameDeserialization();
}
