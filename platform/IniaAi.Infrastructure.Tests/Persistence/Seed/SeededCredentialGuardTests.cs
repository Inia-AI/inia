using System.Text.RegularExpressions;
using IniaAi.Testing;

namespace IniaAi.Infrastructure.Tests.Persistence.Seed;

/// <summary>
/// Publishing the source makes every compiled-in credential a known one, so product code sets no
/// password and no client secret from a literal. Test projects are exempt: their accounts live
/// only in throwaway databases.
/// </summary>
public partial class SeededCredentialGuardTests
{
    [Fact]
    public void ProductSource_SetsNoPasswordOrClientSecretFromALiteral()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> offenders = [.. SourceFiles.Product(repoRoot)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => CredentialLiteral().IsMatch(entry.line))
                .Select(entry => $"{Path.GetRelativePath(repoRoot, file)}:{entry.number}"))];

        Assert.True(offenders.Count == 0, "Read these from configuration instead:\n  " + string.Join("\n  ", offenders));
    }

    [GeneratedRegex(@"SetPassword\(\s*""|CreateAsync\([^,()]+,\s*""|AddPasswordAsync\([^,()]+,\s*""|ClientSecret\s*=\s*""|DEVELOPMENT_DEFAULT|const\s+string\s+\w*(?:Password|PASSWORD|Secret|SECRET)\s*=\s*""")]
    private static partial Regex CredentialLiteral();
}
