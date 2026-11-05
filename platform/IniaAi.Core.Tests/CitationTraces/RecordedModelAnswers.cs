using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Enums;
using NSubstitute;
using NSubstitute.Core;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// Model answers recorded from a live run, citations included, keyed by the step marker each
/// block's system prompt carries. A replay serves every block the answers it gave, in order, so
/// the whole workflow engine runs with no model call. Each answer keeps a fingerprint of the
/// request it answered: a citation points into the request by position, so an answer replayed
/// against a request the engine now builds differently would cite the wrong material silently.
/// </summary>
internal sealed partial class RecordedModelAnswers
{
    private const string STEP_MARKER = "Citation regression step:";
    private const string RECORD_AGAIN = "record the answers again with the live recording test.";

    private readonly Lock _recordingGate = new();

    public Dictionary<string, List<RecordedAnswer>> AnswersByStep { get; init; } = [];

    public static string StepMarker(string step)
    {
        return $"{STEP_MARKER} {step}.";
    }

    /// <summary>A connector that answers each call from the recording and calls no model.</summary>
    public IChatConnector Replaying()
    {
        var servedByStep = new ConcurrentDictionary<string, int>();

        return Connector(call =>
        {
            string step = StepOf(call);
            int turn = servedByStep.AddOrUpdate(step, 0, (_, served) => served + 1);

            if (!AnswersByStep.TryGetValue(step, out List<RecordedAnswer>? answers) || turn >= answers.Count)
            {
                throw new InvalidOperationException(
                    $"No recorded answer for step '{step}', call {turn + 1}. The workflow now asks the model more than "
                    + $"the recording saw: {RECORD_AGAIN}");
            }

            if (answers[turn].Request != FingerprintOf(call))
            {
                throw new InvalidOperationException(
                    $"Step '{step}', call {turn + 1}: the engine now builds a different request than the one this answer "
                    + $"was recorded for, so its citations would point at the wrong material: {RECORD_AGAIN}");
            }

            return Task.FromResult(answers[turn].ToResponse());
        });
    }

    /// <summary>A connector that passes each call to <paramref name="live"/> and keeps its answer.</summary>
    public IChatConnector Recording(IChatConnector live)
    {
        return Connector(async call =>
        {
            ResponseContentSequence response = await live.GetResponseAsync(
                call.ArgAt<RequestContentSequence>(0),
                call.ArgAt<string?>(1),
                call.ArgAt<int>(2),
                call.ArgAt<bool>(3),
                call.ArgAt<string?>(4),
                call.ArgAt<ModelFamily?>(5),
                call.ArgAt<ModelEffort?>(6),
                call.ArgAt<CancellationToken>(7));

            lock (_recordingGate)
            {
                string step = StepOf(call);
                if (!AnswersByStep.TryGetValue(step, out List<RecordedAnswer>? answers))
                {
                    answers = [];
                    AnswersByStep[step] = answers;
                }

                answers.Add(RecordedAnswer.From(FingerprintOf(call), response));
            }

            return response;
        });
    }

    private static IChatConnector Connector(Func<CallInfo, Task<ResponseContentSequence>> answer)
    {
        IChatConnector connector = Substitute.For<IChatConnector>();
        _ = connector.GetResponseAsync(
                Arg.Any<RequestContentSequence>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<ModelFamily?>(),
                Arg.Any<ModelEffort?>(),
                Arg.Any<CancellationToken>())
            .Returns(answer);
        return connector;
    }

    /// <summary>
    /// The system prompt and every request item's kind, title and text. Ids are left out: the
    /// engine mints page and block ids per run.
    /// </summary>
    private static string FingerprintOf(CallInfo call)
    {
        var request = new StringBuilder(call.ArgAt<string?>(1));

        foreach (RequestCommonContent content in call.ArgAt<RequestContentSequence>(0))
        {
            _ = request.Append('\n')
                .Append(content.Type).Append('|')
                .Append(content.IsInputContext).Append('|')
                .Append(content.IsBlockReference).Append('|')
                .Append(content.Title).Append('|')
                .Append(content.TextContent);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ToString())));
    }

    private static string StepOf(CallInfo call)
    {
        string systemPrompt = call.ArgAt<string?>(1) ?? string.Empty;
        Match match = StepRegex().Match(systemPrompt);

        return match.Success
            ? match.Groups["step"].Value
            : throw new InvalidOperationException($"The system prompt carries no '{STEP_MARKER}' marker: {systemPrompt}");
    }

    [GeneratedRegex(STEP_MARKER + @" (?<step>[a-z-]+)\.")]
    private static partial Regex StepRegex();
}

internal sealed record RecordedAnswer(string Request, List<RecordedContent> Contents, string? StopReason)
{
    public static RecordedAnswer From(string request, ResponseContentSequence response)
    {
        List<RecordedContent> contents = [.. response
            .Where(content => content.Type is CommonContentType.Text)
            .Select(content => new RecordedContent(
                content.TextContent ?? string.Empty,
                [.. (content.SourceReferences ?? []).Select(reference => new RecordedReference(reference.Index, reference.Title, reference.CitedText))]))];

        return new RecordedAnswer(
            request,
            contents,
            response.Metadata.TryGetValue(ResponseContentSequence.STOP_REASON_METADATA_KEY, out object? stopReason) ? stopReason.ToString() : null);
    }

    public ResponseContentSequence ToResponse()
    {
        var response = new ResponseContentSequence();

        foreach (RecordedContent content in Contents)
        {
            response.Add(new ResponseCommonContent(content.Text)
            {
                SourceReferences = content.References.Count == 0
                    ? null
                    : [.. content.References.Select(reference => new SourceReference
                    {
                        Index = reference.Index,
                        Title = reference.Title,
                        CitedText = reference.CitedText,
                    })],
            });
        }

        if (StopReason is not null)
        {
            response.Metadata[ResponseContentSequence.STOP_REASON_METADATA_KEY] = StopReason;
        }

        return response;
    }
}

internal sealed record RecordedContent(string Text, List<RecordedReference> References);

internal sealed record RecordedReference(int Index, string? Title, string? CitedText);
