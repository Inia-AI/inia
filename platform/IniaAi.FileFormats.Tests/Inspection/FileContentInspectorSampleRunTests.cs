namespace IniaAi.FileFormats.Tests.Inspection;

/// <summary>
/// Runs the inspector over a directory of real documents, read in place, and fails on any
/// refusal of a file whose extension the platform accepts. Skips unless
/// <c>INIA_INSPECTOR_SAMPLE_DIR</c> names the directory, so CI never depends on local files.
/// <c>INIA_INSPECTOR_SAMPLE_PER_EXTENSION</c> caps how many files of each extension are read,
/// which keeps a pass over a cloud-synced tree from downloading it.
/// </summary>
public class FileContentInspectorSampleRunTests
{
    private const string SAMPLE_DIRECTORY_VARIABLE = "INIA_INSPECTOR_SAMPLE_DIR";
    private const string FILES_PER_EXTENSION_VARIABLE = "INIA_INSPECTOR_SAMPLE_PER_EXTENSION";
    private const int DEFAULT_FILES_PER_EXTENSION = 100;
    private const long MAX_FILE_BYTES = 30L * 1024 * 1024;

    private readonly ITestOutputHelper _output;

    public FileContentInspectorSampleRunTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void EveryRealDocumentWithAnAcceptedExtension_IsAdmitted()
    {
        string? root = Environment.GetEnvironmentVariable(SAMPLE_DIRECTORY_VARIABLE);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(root), $"{SAMPLE_DIRECTORY_VARIABLE} is not set");

        int perExtension = int.TryParse(Environment.GetEnvironmentVariable(FILES_PER_EXTENSION_VARIABLE), out int configured) ? configured : DEFAULT_FILES_PER_EXTENSION;
        InspectionFixtures.SweepResult sweep = InspectionFixtures.Sweep(root!, perExtension, MAX_FILE_BYTES);

        string distribution = string.Join(Environment.NewLine, sweep.Verdicts.Select(pair => $"{pair.Value,6}  {pair.Key}"));
        _output.WriteLine($"Inspected {sweep.Inspected} files under {root}:{Environment.NewLine}{distribution}");

        Assert.True(sweep.Inspected > 0, $"no files found under {root}");
        Assert.True(sweep.FalseRefusals.Count == 0,
            $"{sweep.FalseRefusals.Count} of {sweep.Inspected} files with an accepted extension were refused:{Environment.NewLine}{string.Join(Environment.NewLine, sweep.FalseRefusals)}");
    }
}
