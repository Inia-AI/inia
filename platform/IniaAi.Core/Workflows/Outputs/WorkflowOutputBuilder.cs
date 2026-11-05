using ErrorOr;
using IniaAi.Common.Text;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Outputs;

public partial class WorkflowOutputBuilder : IWorkflowOutputBuilder
{
    public Task<ReportParagraph?> BuildParagraphAsync(
        Block block,
        WorkflowOutputContext? context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null)
    {
        ArgumentNullException.ThrowIfNull(block);

        string? content = block.BlockResponse?.RawResponse;

        BlockCitedSourcesCollector? citedSourcesCollector = annotationsContext is null
            ? null
            : new BlockCitedSourcesCollector(annotationsContext);

        // Build citations from facts
        List<Citation> citations = BuildCitations(
            blockAnnotations?.Facts ?? [],
            content,
            annotationsContext,
            citedSourcesCollector,
            blockAnnotations?.Trace);

        // Apply fact markers to content with citation IDs
        // Facts are applied smallest-first, so granular citations take precedence
        content = content is not null && blockAnnotations is not null
            ? BlockAnnotationProcessor.ApplyFactsWithIds(
                content,
                blockAnnotations.Facts,
                citations,
                blockAnnotations.Trace)
            : content;

        citations = FilterOrphanCitations(citations, content, blockAnnotations?.Trace);

        var paragraph = new ReportParagraph
        {
            SectionId = context?.CurrentReportSection?.Id ?? context?.CurrentSectionId ?? string.Empty,
            BlockId = block.Id,
            Kind = ReportParagraphKind.Paragraph,
            Order = block.Order,
            Title = block.Name,
            Subtitle = block.ExecutionState?.OutputTitle,
            Confidence = block.ExecutionState?.ConfidenceScore
                ?? WorkflowOutputConstants.DEFAULT_CONFIDENCE,
            Content = content,
            Citations = citations,
            CitedSourcesByBlock = citedSourcesCollector?.ListsReachedBy(citations) ?? [],
            ReplacementTag = block.ReplacementTag,
        };

        foreach (string source in block.ExecutionState?.Sources ?? [])
        {
            paragraph.Sources.Add(source);
        }

        ReportParagraph? result = block.Type == BlockTypes.DynamicChart
            ? BuildChartParagraph(paragraph, block)
            : paragraph;

        return Task.FromResult(result);
    }

    public ErrorOr<ReportDataOutput> BuildDataOutput(
        Block block,
        WorkflowOutputContext? context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null)
    {
        ArgumentNullException.ThrowIfNull(block);

        string sends = $"The block \"{block.Name}\" sends its result to other systems as \"{block.DataOutputName}\"";

        // The output format makes the model's JSON checked and retried once; without it a data
        // output would depend on the model happening to answer in JSON.
        if (block.Properties is AiQueryProperties aiQuery && !aiQuery.ProducesValidatedJson())
        {
            return Error.Validation(
                description: $"{sends}, but its output format is not JSON. Set Output format validation to JSON or JSON in Tag, then run the block again.");
        }

        string? json = block.BlockResponse?.JsonResponse ?? block.BlockResponse?.RawResponse;
        if (string.IsNullOrWhiteSpace(json))
        {
            return Error.Validation(description: $"{sends}, but it produced no result. Check what the block reads, then run it again.");
        }

        using JsonDocument? document = TryParseJson(json);
        if (document is null)
        {
            return Error.Validation(description: $"{sends}, but its result is not JSON. Make the block return JSON, then run it again.");
        }

        IReadOnlyList<Fact> facts = blockAnnotations?.Facts ?? [];
        BlockCitedSourcesCollector? citedSourcesCollector = annotationsContext is null
            ? null
            : new BlockCitedSourcesCollector(annotationsContext);

        List<Citation> citations = BuildCitations(facts, json, annotationsContext, citedSourcesCollector, trace: null);
        List<DataOutputCitation> citedValues = citations.Count == 0
            ? []
            : CiteValues(document.RootElement, facts, citations);

        return new ReportDataOutput
        {
            WorkflowReportId = ReportIdOf(context) ?? string.Empty,
            BlockId = block.Id,
            Name = block.DataOutputName ?? string.Empty,
            Json = json,
            Citations = citedValues,
            CitedSourcesByBlock = citedSourcesCollector?.ListsReachedBy(citedValues) ?? [],
        };
    }

    private static JsonDocument? TryParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Matches the facts against each value of the JSON on its own, with the matching the report
    /// uses, so a citation names the value it backs and the JSON itself stays free of markers.
    /// </summary>
    private static List<DataOutputCitation> CiteValues(
        JsonElement root,
        IReadOnlyList<Fact> facts,
        List<Citation> citations)
    {
        var citedValues = new List<DataOutputCitation>();
        if (BlockAnnotationProcessor.FactMarkers.Prepare(facts, citations) is not { } markers)
        {
            return citedValues;
        }

        Dictionary<string, Citation> citationsById = citations.ToDictionary(citation => citation.Id);
        var values = new List<(string Path, string Text)>();
        CollectValuesWithDigits(root, string.Empty, values);

        foreach ((string path, string text) in values)
        {
            foreach (Match marker in FactMarkerRegex().Matches(markers.Apply(text)))
            {
                if (!citationsById.TryGetValue(marker.Groups[1].Value, out Citation? citation))
                {
                    continue;
                }

                citedValues.Add(new DataOutputCitation
                {
                    Id = citation.Id,
                    Sources = citation.Sources,
                    IsNumerical = citation.IsNumerical,
                    Path = path,
                    Value = marker.Groups[2].Value,
                });
            }
        }

        return citedValues;
    }

    /// <summary>Every text and number value that holds a digit: only those can carry a cited figure.</summary>
    private static void CollectValuesWithDigits(JsonElement element, string path, List<(string Path, string Text)> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    CollectValuesWithDigits(property.Value, JsonPointer.Append(path, property.Name), values);
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CollectValuesWithDigits(item, $"{path}/{index}", values);
                    index++;
                }

                break;

            case JsonValueKind.String or JsonValueKind.Number:
                string text = element.ValueKind is JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.GetRawText();
                if (text.AsSpan().ContainsAnyInRange('0', '9'))
                {
                    values.Add((path, text));
                }

                break;

            default:
                break;
        }
    }

    private static string? ReportIdOf(WorkflowOutputContext? context)
    {
        return context?.CurrentWorkflowReport?.Id ?? context?.RootElementId;
    }

    public Task<ReportSection?> BuildSectionAsync(
        Section section,
        WorkflowOutputContext? context)
    {
        ArgumentNullException.ThrowIfNull(section);

        string? reportId = ReportIdOf(context);
        if (reportId is null)
        {
            return Task.FromResult<ReportSection?>(null);
        }

        var reportSection = new ReportSection
        {
            WorkflowReportId = reportId,
            Order = section.Order,
            Title = section.Title,
            ExcludeFromWordExport = section.Properties.ExcludeFromWordExport,
        };

        if (section.Configuration?.GridLayout is not null
            && section.Configuration.GridLayout.Mode == "grid")
        {
            reportSection.Layout = new GridLayoutOutput
            {
                Mode = section.Configuration.GridLayout.Mode,
                Columns = section.Configuration.GridLayout.Columns,
                Elements = section.Configuration.GridLayout.Elements ?? [],
            };
        }

        return Task.FromResult<ReportSection?>(reportSection);
    }

    private static ReportParagraph? BuildChartParagraph(ReportParagraph paragraph, Block block)
    {
        string? response = block.BlockResponse?.RawResponse;

        if (string.IsNullOrEmpty(response))
        {
            return null;
        }

        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            ChartConfiguration? chartConfig = JsonSerializer.Deserialize<ChartConfiguration>(response, jsonOptions);

            if (chartConfig is null)
            {
                return null;
            }

            var chartOutput = new ChartOutput
            {
                Name = chartConfig.Title,
                ChartData = chartConfig.Data,
                ChartConfig = chartConfig
            };

            paragraph.Kind = ReportParagraphKind.DynamicChart;
            paragraph.ChartPayload = chartOutput;
            paragraph.Content = $"Chart: {chartOutput.Name}";

            return paragraph;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a list of citations from citable numerical facts only.
    /// Broad facts and bare single digits are skipped — they don't produce citations.
    /// Deduplicates by fact value — same text shares one citation.
    /// After building, flattens sources: prefers direct doc/url,
    /// pulls up from upstream tree, falls back to block sources.
    /// </summary>
    private static List<Citation> BuildCitations(
        IReadOnlyList<Fact> facts,
        string? content,
        WorkflowAnnotationsContext? annotationsContext,
        BlockCitedSourcesCollector? citedSourcesCollector,
        CitationTraceRecorder? trace)
    {
        if (facts.Count == 0)
        {
            return [];
        }

        var citationsByFactValue = new Dictionary<string, Citation>();

        foreach (Fact fact in facts)
        {
            // Rule 1: only citable numerical facts get citations
            if (!BlockAnnotationProcessor.IsCitableNumericalFact(fact))
            {
                continue;
            }

            if (!citationsByFactValue.TryGetValue(fact.Value, out Citation? citation))
            {
                citation = new Citation
                {
                    Id = $"c{citationsByFactValue.Count}",
                    Sources = [],
                    IsNumerical = true
                };
                citationsByFactValue[fact.Value] = citation;
            }

            foreach (BaseSource source in fact.Sources)
            {
                if (IsSourceAlreadyPresent(citation.Sources, source))
                {
                    continue;
                }

                // For block sources, expand upstream sources recursively.
                // Pass factValue to scope matching — prevents
                // "$183,866 million" from picking up "94.6%"'s sources when both
                // appear in the same paragraph CitedText.
                citation.Sources.Add(source is BlockSource blockSource && annotationsContext is not null
                    ? BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, annotationsContext, fact.Value)
                    : source);
            }
        }

        // Rules 2-4: flatten sources — prefer doc/url, pull from upstream, fallback to block
        foreach (KeyValuePair<string, Citation> entry in citationsByFactValue)
        {
            TracedFlattening flattening = BlockAnnotationProcessor.FlattenCitationSources(
                entry.Value,
                entry.Key,
                string.IsNullOrEmpty(content) ? [] : ClaimContext.WordsAroundValue(content, entry.Key));
            BlockAnnotationProcessor.BoundCitationSources(entry.Value, entry.Key);

            if (citedSourcesCollector is not null)
            {
                BlockAnnotationProcessor.MarkMatchingSources(entry.Value, entry.Key, citedSourcesCollector);
            }

            trace?.Citation(entry.Value, entry.Key, flattening);
        }

        return [.. citationsByFactValue.Values];
    }

    /// <summary>
    /// Filters out orphan citations that don't appear in the final content.
    /// Only numerical citations are filtered (they must have a &lt;fact&gt; tag in content).
    /// Non-numerical (broad) citations are always kept in the array for frontend/debugging use.
    /// </summary>
    private static List<Citation> FilterOrphanCitations(List<Citation> citations, string? content, CitationTraceRecorder? trace)
    {
        if (string.IsNullOrEmpty(content) || citations.Count == 0)
        {
            return citations;
        }

        // Find all citation IDs that appear in the content as <fact id="cX">
        var usedCitationIds = new HashSet<string>();
        Regex regex = FactIdRegex();
        MatchCollection matches = regex.Matches(content);

        foreach (Match match in matches)
        {
            _ = usedCitationIds.Add(match.Groups[1].Value);
        }

        // Keep non-numerical (broad) citations always; filter orphan numerical citations
        foreach (Citation orphan in citations.Where(c => c.IsNumerical && !usedCitationIds.Contains(c.Id)))
        {
            trace?.Orphan(orphan.Id);
        }

        return [.. citations.Where(c => !c.IsNumerical || usedCitationIds.Contains(c.Id))];
    }

    /// <summary>
    /// Checks if a source with the same key is already present in the list.
    /// </summary>
    private static bool IsSourceAlreadyPresent(List<BaseSource> sources, BaseSource source)
    {
        return sources.Any(s => s.SourceKey == source.SourceKey);
    }

    [GeneratedRegex(@"<fact\s+id=""([^""]+)"">")]
    private static partial Regex FactIdRegex();

    [GeneratedRegex(@"<fact\s+id=""([^""]+)"">(.*?)</fact>", RegexOptions.Singleline)]
    private static partial Regex FactMarkerRegex();
}
