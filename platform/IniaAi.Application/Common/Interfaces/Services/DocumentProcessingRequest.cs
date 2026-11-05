namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// One document to run through the processing pipeline standalone — no execution, no job.
/// </summary>
public sealed record DocumentProcessingRequest(string OrganizationId, string DocumentId, string UserId);
