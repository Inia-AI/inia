using IniaAi.Configuration.Enums.Common;

namespace IniaAi.Configuration.Platform;

public class IniaAiSettings
{
    public int MaxFileSizeMb { get; set; } = 100;

    public int BatchSizeForEmbeddings { get; set; } = 50;

    public int? LlmMaxDegreeOfParallelism { get; set; }

    public ModelTier SummarizationModelType { get; set; } = ModelTier.Basic;

    public string NoDataForPlaceHolderText { get; set; } = "No data available";

    public int MaxConcurrentJobs { get; set; } = 6;

    public int MaxRetriesForTopicsGeneration { get; set; } = 3;

    public int WordCountPerPage { get; set; } = 250;
}
