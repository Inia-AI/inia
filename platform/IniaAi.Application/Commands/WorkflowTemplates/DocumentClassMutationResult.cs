using IniaAi.Application.Common.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates;

public record DocumentClassMutationResult(
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    string Key,
    DocumentClassView? DocumentClass = null);
