namespace IniaAi.WebApi.Contracts.Dto.Citations;

/// <summary>
/// Every page and link that one block, or any block upstream of it, cited during the run.
/// A block citation's reader follows this list to the sources behind the block.
/// </summary>
public class BlockCitedSourcesDto
{
    /// <summary>
    /// Gets or sets the identifier of the block the list belongs to.
    /// Matches <see cref="BlockCitationSourceDto.BlockId"/>.
    /// </summary>
    public string BlockId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document pages and web links, one entry per page or link, in chain
    /// order: the block's own sources first.
    /// </summary>
    public List<CitationSourceDto> Sources { get; set; } = [];
}
