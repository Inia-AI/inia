using System.Text;

namespace IniaAi.WebApi.IntegrationTests;

/// <summary>
/// Keeps what a test saw as a file in the folder <see cref="FOLDER_VARIABLE"/> names, so a review can cite it. Without
/// the variable nothing is written.
/// </summary>
internal static class TestEvidence
{
    public const string FOLDER_VARIABLE = "INIA_TEST_EVIDENCE_DIR";

    public static Task WriteAsync(string fileName, string text)
    {
        return SaveAsync(fileName, text, append: false);
    }

    public static Task AppendAsync(string fileName, string text)
    {
        return SaveAsync(fileName, text, append: true);
    }

    private static async Task SaveAsync(string fileName, string text, bool append)
    {
        if (Environment.GetEnvironmentVariable(FOLDER_VARIABLE) is not { Length: > 0 } folder)
        {
            return;
        }

        _ = Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        if (append)
        {
            await File.AppendAllTextAsync(path, text, Encoding.UTF8, cancellationToken);
        }
        else
        {
            await File.WriteAllTextAsync(path, text, Encoding.UTF8, cancellationToken);
        }
    }
}
