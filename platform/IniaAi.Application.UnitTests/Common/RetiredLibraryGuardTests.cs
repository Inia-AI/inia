using IniaAi.Testing;

namespace IniaAi.Application.UnitTests.Common;

/// <summary>
/// The platform retired libraries whose releases a bank may only run under a paid licence, so no
/// package list, project file or source file names one again. A package that pulls one in as a
/// dependency does not name it here: check `dotnet list package --include-transitive` when adding one.
/// </summary>
public class RetiredLibraryGuardTests
{
    private const string GUARD_FILE = "IniaAi.Application.UnitTests/Common/RetiredLibraryGuardTests.cs";

    private static readonly string[] _retiredLibraries = ["AutoMapper", "MediatR"];
    private static readonly string[] _scannedExtensions = [".cs", ".razor", ".csproj", ".props", ".targets"];
    private static readonly string[] _generatedSuffixes = [".Designer.cs", "ModelSnapshot.cs"];
    private static readonly string[] _skippedDirectories = [".git", ".claude", "bin", "obj", "artifacts", "node_modules"];

    [Fact]
    public void NoPackageListProjectOrSourceFile_NamesARetiredLibrary()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> offenders = [.. FilesUnder(repoRoot)
            .Select(file => Path.GetRelativePath(repoRoot, file).Replace('\\', '/'))
            .Where(file => file != GUARD_FILE)
            .Where(file => NamesARetiredLibrary(File.ReadAllText(Path.Combine(repoRoot, file))))
            .Order()];

        Assert.True(offenders.Count == 0, "These files name a retired library:\n  " + string.Join("\n  ", offenders));
    }

    private static bool NamesARetiredLibrary(string source)
    {
        return _retiredLibraries.Any(library => source.Contains(library, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> FilesUnder(string repoRoot)
    {
        var pending = new Stack<string>();
        pending.Push(repoRoot);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!_skippedDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (_scannedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    && !_generatedSuffixes.Any(suffix => file.EndsWith(suffix, StringComparison.Ordinal)))
                {
                    yield return file;
                }
            }
        }
    }
}
