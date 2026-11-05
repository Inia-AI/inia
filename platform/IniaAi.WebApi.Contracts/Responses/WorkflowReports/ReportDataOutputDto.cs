using IniaAi.WebApi.Contracts.Dto.Citations;
using System.Text.Json;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowReports;

/// <summary>
/// One JSON a run returns beside its report, as the block produced it, with a citation for each
/// cited figure inside it.
/// </summary>
public record ReportDataOutputDto
{
    /// <summary>
    /// Gets the name the workflow gives this output. Names are the keys a reading system relies on.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Gets the JSON itself, as a JSON value rather than a string.
    /// </summary>
    public JsonElement Data { get; init; }

    /// <summary>
    /// Gets the citations of the figures inside <see cref="Data"/>. Each one names the value it
    /// backs by its JSON Pointer.
    /// </summary>
    public List<DataOutputCitationDto> Citations { get; init; } = [];

    /// <summary>
    /// Gets, for each block that a block citation reaches, every page and link that block or its
    /// upstream blocks cited.
    /// </summary>
    public List<BlockCitedSourcesDto> CitedSourcesByBlock { get; init; } = [];
}
