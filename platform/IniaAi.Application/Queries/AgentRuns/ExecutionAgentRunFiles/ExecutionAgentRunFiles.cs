using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.WebApi.Contracts.Responses.AgentRuns;
using System.Linq.Expressions;

namespace IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;

/// <summary>
/// The trace rows of one executed workflow, found through the block each row belongs to, and the
/// projection the list serves. Both queries read rows through <see cref="OfWorkflow"/>, so the list
/// and the download can never disagree on which rows belong to a run.
/// </summary>
public static class ExecutionAgentRunFiles
{
    private const string NDJSON_EXTENSION = ".ndjson";
    private const string JSON_EXTENSION = ".json";
    private const string NDJSON_CONTENT_TYPE = "application/x-ndjson";
    private const string JSON_CONTENT_TYPE = "application/json";
    private const string TEXT_CONTENT_TYPE = "text/plain";
    private const string BINARY_CONTENT_TYPE = "application/octet-stream";

    private static readonly string[] _textExtensions = [".md", ".txt", ".csv", ".log"];

    public static readonly Expression<Func<AgentRunTraceFile, AgentRunFileDto>> ToDto = file => new AgentRunFileDto
    {
        BlockId = file.BlockId ?? string.Empty,
        BlockName = file.Block != null ? file.Block.Name : null,
        ReplacementTag = file.Block != null ? file.Block.ReplacementTag : null,
        RunExecutionId = file.RunExecutionId,
        Attempt = file.Attempt,
        Path = file.Path,
        SizeBytes = file.SizeBytes,
        Sha256 = file.Sha256,
        CollectedAt = file.CollectedAt,
    };

    public static IQueryable<AgentRunTraceFile> OfWorkflow(
        IQueryable<AgentRunTraceFile> files,
        string organizationId,
        string workflowId)
    {
        return files.Where(file =>
            file.OrganizationId == organizationId
            && file.Block != null
            && file.Block.Section != null
            && file.Block.Section.WorkflowId == workflowId);
    }

    public static string ContentTypeOf(string path)
    {
        if (path.EndsWith(NDJSON_EXTENSION, StringComparison.OrdinalIgnoreCase))
        {
            return NDJSON_CONTENT_TYPE;
        }

        if (path.EndsWith(JSON_EXTENSION, StringComparison.OrdinalIgnoreCase))
        {
            return JSON_CONTENT_TYPE;
        }

        return _textExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            ? TEXT_CONTENT_TYPE
            : BINARY_CONTENT_TYPE;
    }
}
