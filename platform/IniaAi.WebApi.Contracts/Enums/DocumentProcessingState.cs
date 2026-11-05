namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Where a document stands on the standalone processing path (upload or paste → pages).
/// </summary>
public enum DocumentProcessingState
{
    /// <summary>Standalone processing was never requested; the document may still carry pages from an execution.</summary>
    NotRequested = 0,

    /// <summary>Processing was requested and has not finished yet.</summary>
    Queued = 1,

    /// <summary>The pipeline finished producing pages.</summary>
    Processed = 2,

    /// <summary>The last standalone attempt failed; <c>processingError</c> says why. Asking again clears it.</summary>
    Failed = 3,
}
