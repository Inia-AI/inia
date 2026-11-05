namespace IniaAi.FileFormats.Excel;

// Provenance/chunking config stamped into a very-hidden sheet on .xlsx files
// produced by block strategies. The presence of the sheet tells downstream
// services (e.g. ExcelSheetAnalysisService) that the file is block-generated
// and should be chunked by rules rather than LLM-analysed.
//
// Stage 1 carries only Source — Rules/ChunkingMode follow once the consumer
// side is wired up. See ExcelAnalysisConfigSheet for the on-disk layout.
public sealed record ExcelAnalysisConfig(
    int SchemaVersion,
    ExcelAnalysisSource Source);

public enum ExcelAnalysisSource
{
    Unknown = 0,
    BlockGenerated = 1,
}
