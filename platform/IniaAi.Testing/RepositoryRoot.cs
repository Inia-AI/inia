namespace IniaAi.Testing;

/// <summary>
/// The platform repository's root, found by walking up from the test binaries to the solution
/// file, for a test that reads the source tree or writes beside it.
/// </summary>
public static class RepositoryRoot
{
    public const string SOLUTION_FILE = "IniaAi.Platform.slnx";

    public static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"{SOLUTION_FILE} was not found above {AppContext.BaseDirectory}.");
    }
}
