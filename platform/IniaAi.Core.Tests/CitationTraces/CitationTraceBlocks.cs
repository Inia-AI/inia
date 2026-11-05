using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.CitationTraces;

internal static class CitationTraceBlocks
{
    private const string API_KEY_HEADER = "x-api-key";
    private const string STUB_API_KEY = "stub-not-actually-used";
    private const string JSON_CONTENT_TYPE = "application/json";

    /// <summary>
    /// A Web API Call block shaped like a production Parallel.ai result block: a GET of the
    /// task's <c>/result</c> URL with the organization's API key header. The key is a stub
    /// because every trace replays a canned body through <see cref="FixtureWebApiCallStrategy"/>.
    /// </summary>
    public static Block ParallelAiResultBlock(
        Section section,
        string name,
        string resultUrl,
        string replacementTag)
    {
        var block = new Block(BlockTypes.WebApiCall, section)
        {
            Name = name,
            ReplacementTag = replacementTag,
            Properties = new WebApiCallProperties
            {
                UrlTemplate = resultUrl,
                HttpMethod = "GET",
                ContentType = JSON_CONTENT_TYPE,
                AcceptHeader = JSON_CONTENT_TYPE,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    [API_KEY_HEADER] = STUB_API_KEY,
                },
            },
        };
        block.SetExecutionState(false, []);

        return block;
    }
}
