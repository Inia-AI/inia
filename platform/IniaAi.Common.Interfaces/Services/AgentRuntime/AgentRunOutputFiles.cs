using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ErrorOr;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Turns a run's answer plus named <c>out/</c> files into one JSON output
/// (<c>{"answer": …, "files": {"&lt;stem&gt;": …}}</c>), so script-made data passes on exactly
/// as the script wrote it instead of being retyped by the model. Any consumer of the runtime
/// can use it: the paths are validated here, the task line is fixed here, and the composition
/// reads only the files it is handed, so the caller decides which run's files those are.
/// </summary>
public static partial class AgentRunOutputFiles
{
    public const string ANSWER_KEY = "answer";
    public const string FILES_KEY = "files";

    public const string ERROR_FILE_MISSING = "AgentRunOutputFiles.FileMissing";
    public const string ERROR_FILE_NOT_JSON = "AgentRunOutputFiles.FileNotJson";
    public const string ERROR_OVER_CAP = "AgentRunOutputFiles.OverCap";

    /// <summary>
    /// The largest composed output accepted. The output becomes a block response that later
    /// blocks substitute into their prompts and that the executed workflow carries, so it is held
    /// well under the collector's 32 MiB pack cap.
    /// </summary>
    public const long MAX_COMPOSED_BYTES = 8L * 1024 * 1024;

    private const string JSON_EXTENSION = ".json";

    /// <summary>
    /// Embedding adds two levels (the root and <c>files</c> or <c>answer</c>), and the
    /// placeholder walk downstream parses at the default depth of 64.
    /// </summary>
    private const int MAX_EMBEDDED_DEPTH = 62;

    private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string StemOf(string path)
    {
        string fileName = path[(path.LastIndexOf('/') + 1)..];
        return fileName[..^JSON_EXTENSION.Length];
    }

    /// <summary>
    /// The out/ files the conclusion's settled run published, and only those: rows of an earlier
    /// pass carry another run id. The attempt is the one the executor collected the run under,
    /// the runtime's own attempt number when it reports one, else the pass.
    /// </summary>
    public static List<AgentRunRecarriedFile> SettledOutFilesOf(AgentRunConclusion conclusion)
    {
        string settledRunId = conclusion.Monitor.ExecutionId;
        int settledPass = conclusion.Attempts.LastOrDefault(
            attempt => string.Equals(attempt.ExecutionId, settledRunId, StringComparison.Ordinal))?.Pass
            ?? AgentRunIds.FIRST_PASS;
        int collectedAttempt = conclusion.Monitor.Attempt > 0 ? conclusion.Monitor.Attempt : settledPass;

        return AgentRunContinuationBuilder.OutFilesOf(conclusion.TraceFiles ?? [], settledRunId, collectedAttempt);
    }

    /// <summary>
    /// The first problem with a declared path list, or null when every path is
    /// <c>out/[dir/]name.json</c> with a name a placeholder can address and no two paths share a stem.
    /// </summary>
    public static string? DescribeFirstPathProblem(IReadOnlyList<string> paths)
    {
        var pathsByStem = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !OutputFilePathRegex().IsMatch(path))
            {
                return $"output file '{path}' must be a path like out/rows.json: under out/, ending in .json, "
                    + "using only letters, digits, '_' and '-' in its folder and file names";
            }

            string stem = StemOf(path);
            if (pathsByStem.TryGetValue(stem, out string? earlier))
            {
                return $"output files '{earlier}' and '{path}' share the name '{stem}', "
                    + "which the block output uses as each file's key; rename one";
            }

            pathsByStem[stem] = path;
        }

        return null;
    }

    /// <summary>
    /// The line appended to the agent's task when files are declared. Fixed wording, so a
    /// template never has to repeat it.
    /// </summary>
    public static string TaskInstruction(IReadOnlyList<string> paths)
    {
        return "Write these files as valid JSON: " + string.Join(", ", paths)
            + ". They are passed on exactly as written beside your answer, so do not repeat their content in your answer.";
    }

    /// <summary>
    /// Composes the output from the answer and the files of the one run the caller selected.
    /// <paramref name="outFiles"/> carry <c>out/</c>-relative paths, as
    /// <see cref="AgentRunContinuationBuilder.OutFilesOf"/> returns them. File bytes are written
    /// verbatim after a validity check, so numbers and text keep the script's exact form; an
    /// answer that is valid JSON is embedded the same way, any other answer as a JSON string.
    /// </summary>
    public static ErrorOr<string> Compose(
        string answer,
        IReadOnlyList<string> paths,
        IReadOnlyList<AgentRunRecarriedFile> outFiles,
        long maxBytes = MAX_COMPOSED_BYTES)
    {
        var filesByPath = new Dictionary<string, AgentRunRecarriedFile>(StringComparer.Ordinal);
        foreach (AgentRunRecarriedFile file in outFiles)
        {
            filesByPath[AgentRunFilePaths.ARTIFACT_PREFIX + file.Path] = file;
        }

        var resolved = new List<(string Stem, ReadOnlyMemory<byte> Json)>(paths.Count);
        long declaredBytes = Encoding.UTF8.GetByteCount(answer);
        foreach (string path in paths)
        {
            if (!filesByPath.TryGetValue(path, out AgentRunRecarriedFile? file))
            {
                return Error.Validation(
                    ERROR_FILE_MISSING,
                    $"The agent did not publish the output file '{path}' (or the platform did not keep it)");
            }

            ReadOnlyMemory<byte> content = WithoutBom(file.Content);
            if (!IsJson(content.Span))
            {
                return Error.Validation(
                    ERROR_FILE_NOT_JSON,
                    $"The output file '{path}' is not valid JSON, or nests deeper than {MAX_EMBEDDED_DEPTH} levels");
            }

            declaredBytes += content.Length;
            if (declaredBytes > maxBytes)
            {
                return OverCap(maxBytes);
            }

            resolved.Add((StemOf(path), content));
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            writer.WriteStartObject();
            writer.WritePropertyName(ANSWER_KEY);
            string trimmedAnswer = answer.Trim();
            byte[] answerBytes = Encoding.UTF8.GetBytes(trimmedAnswer);
            if (trimmedAnswer.Length > 0 && IsJson(answerBytes))
            {
                writer.WriteRawValue(answerBytes, skipInputValidation: true);
            }
            else
            {
                writer.WriteStringValue(answer);
            }

            writer.WritePropertyName(FILES_KEY);
            writer.WriteStartObject();
            foreach ((string stem, ReadOnlyMemory<byte> json) in resolved)
            {
                writer.WritePropertyName(stem);
                writer.WriteRawValue(json.Span, skipInputValidation: true);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        if (buffer.Length > maxBytes)
        {
            return OverCap(maxBytes);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static Error OverCap(long maxBytes)
    {
        return Error.Validation(
            ERROR_OVER_CAP,
            $"The answer plus the output files exceed the {maxBytes / (1024 * 1024)} MiB cap on an Agent block's output");
    }

    private static ReadOnlyMemory<byte> WithoutBom(ReadOnlyMemory<byte> content)
    {
        return content.Span.StartsWith(_utf8Bom) ? content[_utf8Bom.Length..] : content;
    }

    private static bool IsJson(ReadOnlySpan<byte> content)
    {
        var reader = new Utf8JsonReader(content, new JsonReaderOptions { MaxDepth = MAX_EMBEDDED_DEPTH });
        try
        {
            if (!reader.Read())
            {
                return false;
            }

            reader.Skip();

            return !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"\Aout/(?:[A-Za-z0-9_\-]+/)*[A-Za-z0-9_\-]+\.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex OutputFilePathRegex();
}
