using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Graph;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Tools.GraphRag;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Graph;

public partial class GraphQueryService : IGraphQueryService, ITokenAccumulator
{
    private const int MAX_GRAPH_TRAVERSAL_DEPTH = 6;
    private const int MAX_NODES = 1000;

    /// <summary>
    /// Holds data loaded once per QueryAsync call to avoid redundant DB queries across iterations.
    /// </summary>
    private sealed record GraphContext(
        List<GraphNode> NodeEntities,
        Dictionary<string, List<GraphEdge>> IncomingEdges,
        WorkflowExecution Execution,
        Dictionary<string, Block> BlocksByKey,
        HashSet<string> UploadedDocClasses
    );

    private readonly IGraphRepository _graphRepository;
    private readonly IGraphBuilderService _graphBuilderService;
    private readonly IUserDataUnitOfWork _userDataUow;
    private readonly IChatConnector _chatConnector;
    private readonly IToolsSettingsRepository _toolsSettingsRepository;
    private readonly ILogger<GraphQueryService> _logger;
    private readonly IRequestModelUse _modelUse;

    // Token accumulation for tracking (thread-safe for parallel calls)
    private int _accumulatedInputTokens;
    private int _accumulatedOutputTokens;

    #region ITokenAccumulator Implementation

    public int AccumulatedInputTokens => _accumulatedInputTokens;
    public int AccumulatedOutputTokens => _accumulatedOutputTokens;
    public int AccumulatedCacheCreationTokens => 0;
    public int AccumulatedCacheReadTokens => 0;
    public string? LastUsedModelId { get; private set; }

    public void ResetTokenAccumulator()
    {
        _ = Interlocked.Exchange(ref _accumulatedInputTokens, 0);
        _ = Interlocked.Exchange(ref _accumulatedOutputTokens, 0);
        LastUsedModelId = null;
    }

    private void AccumulateTokens(ResponseContentSequence response)
    {
        _ = Interlocked.Add(ref _accumulatedInputTokens, response.InputTokens);
        _ = Interlocked.Add(ref _accumulatedOutputTokens, response.OutputTokens);
        CaptureModelId(response);
    }

    private void CaptureModelId(ResponseContentSequence response)
    {
        if (
            response.Metadata.TryGetValue(
                ModelIdNormalizer.MODEL_METADATA_KEY,
                out object? modelValue
            )
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString)
        )
        {
            LastUsedModelId = modelString;
        }
    }

    #endregion

    public GraphQueryService(
        IGraphRepository graphRepository,
        IGraphBuilderService graphBuilderService,
        IUserDataUnitOfWork userDataUow,
        IChatConnector chatConnector,
        IToolsSettingsRepository toolsSettingsRepository,
        ILogger<GraphQueryService> logger,
        IRequestModelUse modelUse
    )
    {
        _graphRepository = graphRepository;
        _graphBuilderService = graphBuilderService;
        _userDataUow = userDataUow;
        _chatConnector = chatConnector;
        _toolsSettingsRepository = toolsSettingsRepository;
        _logger = logger;
        _modelUse = modelUse;
    }

    public async Task<bool> ValidateAccessAsync(
        string runId,
        string organizationId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            WorkflowExecution? workflowExecution =
                await _userDataUow.WorkflowExecutions.GetByIdAsync(runId, cancellationToken);

            if (workflowExecution is null)
            {
                _logger.LogWarning("Workflow execution not found: runId={RunId}", LogSanitizer.ForLog(runId));
                return false;
            }

            if (workflowExecution.OrganizationId != organizationId)
            {
                _logger.LogWarning(
                    "Organization mismatch: runId={RunId}, expected={Expected}, actual={Actual}",
                    LogSanitizer.ForLog(runId),
                    LogSanitizer.ForLog(organizationId),
                    LogSanitizer.ForLog(workflowExecution.OrganizationId)
                );

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error validating access: runId={RunId}, organizationId={OrganizationId}",
                LogSanitizer.ForLog(runId),
                LogSanitizer.ForLog(organizationId)
            );
            return false;
        }
    }

    public async Task<bool> ValidateTargetExistsAsync(
        string runId,
        string targetTag,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            WorkflowGraph graph = await _graphBuilderService.GetOrBuildGraphAsync(
                runId,
                cancellationToken
            );

            // Block nodes are keyed by Block.Id, but the public API addresses them by ReplacementTag,
            // so a target matches either the node Id or its ReplacementTag.
            return graph.Nodes.Any(n => n.Id == targetTag || n.ReplacementTag == targetTag);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ProvenanceResult> QueryAsync(
        string runId,
        string targetTag,
        string question,
        int startingDepth = 4,
        bool includePreviews = true, // TODO: Not yet implemented — reserved for future use to include/exclude output previews
        CancellationToken cancellationToken = default
    )
    {
        if (startingDepth is < 1 or > MAX_GRAPH_TRAVERSAL_DEPTH)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startingDepth),
                startingDepth,
                $"Starting depth must be between 1 and {MAX_GRAPH_TRAVERSAL_DEPTH}."
            );
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question cannot be empty.", nameof(question));
        }

        if (question.Length > 5000)
        {
            throw new ArgumentException(
                "Question exceeds maximum length of 5000 characters.",
                nameof(question)
            );
        }

        string sanitizedQuestion = SanitizeUserInput(question);

        _logger.LogInformation(
            "Starting graph query: runId={RunId}, targetTag={TargetTag}, startingDepth={StartingDepth}",
            LogSanitizer.ForLog(runId),
            LogSanitizer.ForLog(targetTag),
            startingDepth
        );

        WorkflowGraph graph = await _graphBuilderService.GetOrBuildGraphAsync(
            runId,
            cancellationToken
        );

        _logger.LogDebug(
            "Graph loaded: runId={RunId}, nodeCount={NodeCount}, edgeCount={EdgeCount}",
            LogSanitizer.ForLog(runId),
            graph.Nodes.Count,
            graph.Edges.Count
        );

        // Block nodes are keyed by Block.Id while the public API addresses them by ReplacementTag.
        // Resolve the caller's target to the canonical node id (its Block.Id) so the persisted-graph
        // BFS, which keys on the saved node id, finds it.
        WorkflowGraphNode? targetNode = graph.Nodes
            .FirstOrDefault(n => n.Id == targetTag || n.ReplacementTag == targetTag)
            ?? throw new InvalidOperationException(
                $"Target node '{targetTag}' not found in graph for run '{runId}'"
            );

        targetTag = targetNode.Id;

        GraphContext graphCtx = await LoadGraphContextAsync(runId, cancellationToken);

        int currentDepth = startingDepth;
        ProvenanceData provenance;
        string answer = string.Empty;
        string? action = null;

        for (int iteration = 0; iteration < MAX_GRAPH_TRAVERSAL_DEPTH; iteration++)
        {
            _logger.LogDebug(
                "Iteration {Iteration}: Computing provenance at depth={Depth}",
                iteration + 1,
                currentDepth
            );

            provenance = await GetProvenanceAsync(
                graphCtx,
                targetTag,
                currentDepth,
                cancellationToken
            );

            bool isMaxDepth = currentDepth >= MAX_GRAPH_TRAVERSAL_DEPTH;

            _logger.LogDebug(
                "Calling LLM: depth={Depth}, nodeCount={NodeCount}, outputCount={OutputCount}, isMaxDepth={IsMaxDepth}",
                currentDepth,
                provenance.Nodes.Count,
                provenance.NodeOutputs.Count,
                isMaxDepth
            );

            (answer, action) = await GenerateAnswerAsync(
                provenance,
                sanitizedQuestion,
                currentDepth,
                isMaxDepth,
                cancellationToken
            );

            _logger.LogInformation("LLM response received: action={Action}", LogSanitizer.ForLog(action));

            if (action == "answer" || isMaxDepth)
            {
                _logger.LogInformation(
                    "Graph query completed for runId={RunId}, targetTag={TargetTag}, depth={Depth}, iterations={Iterations}, action={Action}",
                    LogSanitizer.ForLog(runId),
                    LogSanitizer.ForLog(targetTag),
                    currentDepth,
                    iteration + 1,
                    LogSanitizer.ForLog(action)
                );

                return new ProvenanceResult { Answer = answer };
            }

            if (action == "expand")
            {
                currentDepth++;

                _logger.LogInformation(
                    "LLM requested expand for runId={RunId}, targetTag={TargetTag}, expanding to depth={NewDepth}",
                    LogSanitizer.ForLog(runId),
                    LogSanitizer.ForLog(targetTag),
                    currentDepth
                );

                continue;
            }

            // Unknown action — log warning and return the answer from this iteration
            _logger.LogWarning(
                "LLM returned unknown action '{Action}' for runId={RunId}, targetTag={TargetTag}; treating as answer",
                LogSanitizer.ForLog(action),
                LogSanitizer.ForLog(runId),
                LogSanitizer.ForLog(targetTag)
            );

            if (string.IsNullOrWhiteSpace(answer))
            {
                answer = "Unable to determine the answer. Please try rephrasing your question.";
            }

            return new ProvenanceResult { Answer = answer };
        }

        // Safety net: loop exhausted without returning (should not happen in practice)
        _logger.LogWarning(
            "Graph query loop exhausted all iterations for runId={RunId}, targetTag={TargetTag}; returning last answer",
            LogSanitizer.ForLog(runId),
            LogSanitizer.ForLog(targetTag)
        );

        if (string.IsNullOrWhiteSpace(answer))
        {
            answer = "Unable to determine the answer. Please try rephrasing your question.";
        }

        return new ProvenanceResult { Answer = answer };
    }

    /// <summary>
    /// Generates an answer using the LLM, grounded only in the provided provenance data.
    /// </summary>
    private async Task<(string answer, string action)> GenerateAnswerAsync(
        ProvenanceData provenance,
        string question,
        int currentDepth,
        bool isMaxDepth,
        CancellationToken cancellationToken
    )
    {
        GraphRagPrompts prompts =
            await _toolsSettingsRepository.GetGraphRagPromptsAsync() ?? new GraphRagPrompts();

        string systemPrompt = BuildSystemPrompt(prompts);
        string userPrompt = BuildUserPrompt(prompts, provenance, question, currentDepth, isMaxDepth);

        var content = new RequestContentSequence();
        content.Add(userPrompt);

        _modelUse.MarkModelCalled();
        ResponseContentSequence responseContent = await _chatConnector.GetResponseAsync(
            content,
            systemPrompt,
            maxTokens: 4000,
            shouldUseAdvancedModel: true,
            model: null,
            family: null,
            cancellationToken: cancellationToken
        );

        AccumulateTokens(responseContent);

        string response = responseContent.GetPlainText() ?? string.Empty;

        (string? answer, string? action) = ParseLlmResponse(response);

        return (answer, action);
    }

    /// <summary>
    /// Builds the system prompt that constrains the LLM to use only provenance data.
    /// </summary>
    private static string BuildSystemPrompt(GraphRagPrompts prompts)
    {
        return prompts.SystemPrompt ?? string.Empty;
    }

    /// <summary>
    /// Builds the user prompt with serialized provenance data and the user's question.
    /// </summary>
    private static string BuildUserPrompt(
        GraphRagPrompts prompts,
        ProvenanceData provenance,
        string question,
        int currentDepth,
        bool isMaxDepth
    )
    {
        string provenanceJson = JsonSerializer.Serialize(
            provenance,
            new JsonSerializerOptions { WriteIndented = true }
        );

        string userPrompt = prompts.UserPromptTemplate ?? string.Empty;

        userPrompt = userPrompt
            .Replace("{QUESTION}", question)
            .Replace("{PROVENANCE_JSON}", provenanceJson)
            .Replace("{CURRENT_DEPTH}", currentDepth.ToString())
            .Replace("{MAX_DEPTH}", MAX_GRAPH_TRAVERSAL_DEPTH.ToString());

        if (isMaxDepth && !string.IsNullOrWhiteSpace(prompts.MaxDepthWarning))
        {
            string warning = prompts.MaxDepthWarning.Replace(
                "{MAX_DEPTH}",
                MAX_GRAPH_TRAVERSAL_DEPTH.ToString()
            );

            userPrompt = warning + "\n\n" + userPrompt;
        }

        return userPrompt;
    }

    /// <summary>
    /// Parses the LLM response to extract answer and action.
    /// Expects response wrapped in &lt;answer&gt; tags containing JSON.
    /// Falls back to extracting from markdown code blocks if tags not found.
    /// </summary>
    private (string answer, string action) ParseLlmResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("LLM returned an error or empty response: {Error}",
                response.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.Ordinal) ? response : $"{response.Length} characters");
            return ("Unable to determine the answer. The AI service encountered an error. Please try again.", "answer");
        }

        string? jsonContent = Utils.GetStringFromTag(response, "answer");

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            jsonContent = ExtractFromMarkdownCodeBlock(response);
        }

        jsonContent ??= response.Trim();

        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonContent);
            JsonElement root = jsonDoc.RootElement;

            if (
                !root.TryGetProperty("action", out JsonElement actionElement)
                || !root.TryGetProperty("answer", out JsonElement answerElement)
            )
            {
                throw new JsonException("Missing required 'action' or 'answer' properties");
            }

            string action = actionElement.GetString() ?? "answer";
            string answer = answerElement.GetString() ?? string.Empty;

            return (answer, action);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse LLM response: {Length} characters",
                response.Length
            );

            return ("Unable to determine the answer. Please try again.", "answer");
        }
    }

    /// <summary>
    /// Extracts JSON content from markdown code blocks.
    /// Handles ```json ... ``` and ``` ... ``` formats.
    /// </summary>
    private static string? ExtractFromMarkdownCodeBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = GenerateJsonCodeBlockRegex().Match(text);

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Loads all static graph data for a given runId once before the agent loop begins.
    /// </summary>
    private async Task<GraphContext> LoadGraphContextAsync(
        string runId,
        CancellationToken cancellationToken
    )
    {
        List<GraphNode> nodeEntities = await _graphRepository.GetNodesAsync(runId);
        List<GraphEdge> edgeEntities = await _graphRepository.GetEdgesAsync(runId);

        var incomingEdges = new Dictionary<string, List<GraphEdge>>();

        foreach (GraphEdge edge in edgeEntities)
        {
            if (!incomingEdges.TryGetValue(edge.TargetId, out List<GraphEdge>? graphEdges))
            {
                graphEdges = [];
                incomingEdges[edge.TargetId] = graphEdges;
            }

            graphEdges.Add(edge);
        }

        WorkflowExecution execution =
            await _userDataUow.WorkflowExecutions.GetByIdAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"WorkflowExecution not found for runId={runId}"
            );

        if (execution.WorkflowId is null)
        {
            throw new InvalidOperationException($"WorkflowId is null for execution {runId}");
        }

        HashSet<string> uploadedDocClasses = await GetUploadedDocumentClasses(
            runId,
            execution.JobId
        );

        Dictionary<string, Block> blocksByKey = await CreateBlockLookupDictionary(
            execution.WorkflowId,
            cancellationToken
        );

        return new GraphContext(
            nodeEntities,
            incomingEdges,
            execution,
            blocksByKey,
            uploadedDocClasses
        );
    }

    /// <summary>
    /// Performs BFS traversal to find upstream provenance for a target node.
    /// </summary>
    private async Task<ProvenanceData> GetProvenanceAsync(
        GraphContext ctx,
        string targetTag,
        int maxDepth,
        CancellationToken cancellationToken
    )
    {
        var queue = new Queue<(string nodeKey, int depth)>();
        var visited = new HashSet<string>();
        var visitedNodes = new List<GraphNode>();
        var visitedEdges = new List<GraphEdge>();

        // Start BFS from target node
        queue.Enqueue((targetTag, 0));
        _ = visited.Add(targetTag);

        // Add target node to visited list
        GraphNode? targetNode =
            ctx.NodeEntities.FirstOrDefault(n => n.Key == targetTag)
            ?? throw new InvalidOperationException(
                $"Target node '{targetTag}' not found in run '{ctx.Execution.Id}'"
            );
        visitedNodes.Add(targetNode);

        // BFS traversal (Upstream)
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (string? currentKey, int currentDepth) = queue.Dequeue();

            if (currentDepth >= maxDepth)
            {
                continue;
            }

            // Find all incoming edges to current node
            if (!ctx.IncomingEdges.ContainsKey(currentKey))
            {
                continue; // No incoming edges, this is a source node
            }

            foreach (GraphEdge edge in ctx.IncomingEdges[currentKey])
            {
                string sourceKey = edge.SourceId;

                // Track the edge
                if (!visitedEdges.Contains(edge))
                {
                    visitedEdges.Add(edge);
                }

                if (visited.Contains(sourceKey))
                {
                    continue;
                }

                if (visited.Count >= MAX_NODES)
                {
                    throw new InvalidOperationException(
                        $"Graph traversal exceeded maximum node limit of {MAX_NODES}. "
                            + "The graph may be too large or contain cycles."
                    );
                }

                _ = visited.Add(sourceKey);
                queue.Enqueue((sourceKey, currentDepth + 1));

                // Add source node to visited list
                GraphNode? sourceNode = ctx.NodeEntities.FirstOrDefault(n => n.Key == sourceKey);

                if (sourceNode is not null)
                {
                    visitedNodes.Add(sourceNode);
                }
            }
        }

        var nodeDtos = visitedNodes
            .Select(n => new ProvenanceNode
            {
                Key = n.Key,
                Kind = n.Kind,
                Label = n.Label,
                Order = n.Order,
                DocumentClass = FilterDocumentClasses(n.DocumentClass, ctx.UploadedDocClasses),
            })
            .ToList();

        // Generate node outputs on-demand from BlockResponse table
        var nodeOutputDtos = new List<NodeOutput>();

        _logger.LogInformation(
            "Generating on-demand previews for {VisitedCount} visited nodes",
            visited.Count
        );

        HashSet<string> executedKeys = GetExecutedKeys(visited, ctx.BlocksByKey);

        _logger.LogInformation(
            "Filtered to {ExecutedCount} executed blocks out of {VisitedCount} visited (removed {SkippedCount} non-executed)",
            executedKeys.Count,
            visited.Count,
            visited.Count - executedKeys.Count
        );

        // Filter to only executed blocks for preview generation
        HashSet<string> executedVisited = executedKeys;

        // Filter node DTOs to only include executed blocks and document inputs
        nodeDtos =
        [
            .. nodeDtos.Where(n => n.Kind == "document_input" || executedVisited.Contains(n.Key)),
        ];

        await GenerateOnDemandPreviews(
            executedVisited,
            visitedNodes,
            nodeOutputDtos,
            ctx.BlocksByKey,
            cancellationToken
        );

        return new ProvenanceData
        {
            Target = targetTag,
            Depth = executedVisited.Count > 0 ? maxDepth : 0,
            Nodes = nodeDtos,
            NodeOutputs = nodeOutputDtos,
        };
    }

    private static HashSet<string> GetExecutedKeys(
        HashSet<string> visited,
        Dictionary<string, Block> blocksByKey
    )
    {
        var executedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string visitedKey in visited)
        {
            if (
                !blocksByKey.TryGetValue(visitedKey, out Block? block)
                || block?.BlockResponseId is null
            )
            {
                continue;
            }

            _ = executedKeys.Add(visitedKey);
        }

        return executedKeys;
    }

    private async Task<Dictionary<string, Block>> CreateBlockLookupDictionary(
        string workflowId,
        CancellationToken cancellationToken
    )
    {
        Dictionary<int, List<Block>> allBlocks =
            await _userDataUow.Workflows.GetAllBlocksByWorkflowIdAsync(
                workflowId,
                cancellationToken
            );

        var blocksByKey = new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);

        foreach ((_, List<Block> blocks) in allBlocks.OrderBy(b => b.Key))
        {
            foreach (Block block in blocks)
            {
                if (!string.IsNullOrWhiteSpace(block.ReplacementTag))
                {
                    _ = blocksByKey.TryAdd(block.ReplacementTag, block);
                }

                _ = blocksByKey.TryAdd(block.Id, block);
            }
        }

        _logger.LogInformation(
            "Loaded {BlockCount} blocks into lookup with {KeyCount} unique keys",
            allBlocks.Count,
            blocksByKey.Count
        );

        return blocksByKey;
    }

    private async Task<HashSet<string>> GetUploadedDocumentClasses(string runId, string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogWarning(
                "No JobId found for execution {RunId}, cannot filter document classes",
                LogSanitizer.ForLog(runId)
            );

            return [];
        }

        var uploadedDocClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<DocumentInfo> documentInfos = await _userDataUow
            .Repository<DocumentInfo>()
            .FindAsync(DocumentInfo.IsRunInputOf(jobId));

        foreach (DocumentInfo docInfo in documentInfos)
        {
            if (string.IsNullOrWhiteSpace(docInfo.DocumentClass))
            {
                continue;
            }

            _ = uploadedDocClasses.Add(docInfo.DocumentClass);
        }

        _logger.LogInformation(
            "Found {Count} uploaded document classes for JobId {JobId}: {Classes}",
            uploadedDocClasses.Count,
            jobId,
            string.Join(", ", uploadedDocClasses)
        );

        return uploadedDocClasses;
    }

    private async Task GenerateOnDemandPreviews(
        HashSet<string> visited,
        List<GraphNode> visitedNodes,
        List<NodeOutput> nodeOutputDtos,
        Dictionary<string, Block> blocksByKey,
        CancellationToken cancellationToken
    )
    {
        int generatedCount = 0;

        foreach (string visitedKey in visited)
        {
            if (!blocksByKey.TryGetValue(visitedKey, out Block? matchingBlock))
            {
                _logger.LogDebug("No block found for key {Key}", visitedKey);

                continue;
            }

            if (matchingBlock.BlockResponseId is null)
            {
                _logger.LogDebug("Block {Key} has no BlockResponseId", visitedKey);

                continue;
            }

            BlockResponse? blockResponse = await _userDataUow
                .Repository<BlockResponse>()
                .GetByIdAsync(matchingBlock.BlockResponseId, cancellationToken);

            if (blockResponse is null)
            {
                _logger.LogDebug(
                    "No BlockResponse found for Block {Key} with BlockResponseId {BlockResponseId}",
                    visitedKey,
                    matchingBlock.BlockResponseId
                );

                continue;
            }

            string? raw =
                blockResponse.RawResponse
                ?? blockResponse.JsonResponse
                ?? blockResponse.MarkdownResponse
                ?? blockResponse.HtmlResponse;

            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogDebug("No output content found for {Key}", visitedKey);

                continue;
            }

            // Sanitize and create preview
            (string? preview, int _) = PreviewSanitizer.Sanitize(raw, 3000);

            if (string.IsNullOrEmpty(preview))
            {
                _logger.LogDebug("Sanitized preview is empty for {Key}", visitedKey);

                continue;
            }

            GraphNode? node = visitedNodes.FirstOrDefault(n => n.Key == visitedKey);

            nodeOutputDtos.Add(
                new NodeOutput
                {
                    Key = visitedKey,
                    Preview = preview,
                    FullLength = null,
                    DocumentClass = node?.DocumentClass,
                }
            );

            generatedCount++;

            _logger.LogDebug(
                "Generated preview for {Key}: {PreviewLength} chars",
                visitedKey,
                preview.Length
            );
        }

        _logger.LogInformation(
            "Generated {GeneratedCount} on-demand previews out of {VisitedCount} visited nodes",
            generatedCount,
            visited.Count
        );
    }

    /// <summary>
    /// Sanitizes user input to prevent prompt injection attacks.
    /// </summary>
    internal static string SanitizeUserInput(string input)
    {
        return string.IsNullOrWhiteSpace(input)
            ? input
            : input
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("{{", "{ {")
            .Replace("}}", "} }")
            .Replace("```", "` ` `");
    }

    /// <summary>
    /// Filters a comma-separated DocumentClass string to only include classes that were actually uploaded.
    /// </summary>
    /// <param name="documentClasses">Comma-separated document classes (e.g., "Annual Report, Supplementary Files")</param>
    /// <param name="uploadedClasses">Set of actually uploaded document classes</param>
    /// <returns>Filtered comma-separated string, or null if no classes match</returns>
    private static string? FilterDocumentClasses(
        string? documentClasses,
        HashSet<string> uploadedClasses
    )
    {
        if (string.IsNullOrWhiteSpace(documentClasses))
        {
            return null;
        }

        if (uploadedClasses.Count == 0)
        {
            return null; // No uploads means filter everything out
        }

        // Split, trim, and filter
        var filteredClasses = documentClasses
            .Split(',')
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c) && uploadedClasses.Contains(c))
            .ToList();

        return filteredClasses.Count > 0 ? string.Join(", ", filteredClasses) : null;
    }

    [GeneratedRegex(@"```(?:json)?\s*\n?([\s\S]*?)\n?```", RegexOptions.Singleline
    )]
    private static partial Regex GenerateJsonCodeBlockRegex();
}
