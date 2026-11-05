namespace IniaAi.Core.Services.DocumentClassValidation;

public class DocumentClassValidationResult
{
    public bool IsValid { get; init; }
    public bool HasWarnings { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<DocumentClassStatus> DocumentClassStatuses { get; init; } = [];
}

public class DocumentClassStatus
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Requirement { get; init; }
    public bool ShouldBeSingular { get; init; }
    public int UploadedCount { get; init; }
    public bool IsSatisfied { get; init; }
    public string? ValidationMessage { get; init; }
}
