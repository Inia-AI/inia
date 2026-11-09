using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Documents;

/// <param name="FailureKind">One of <see cref="WorkflowExecutionFailureKinds"/>.</param>
/// <param name="FailureDetail">What happened and what to do next, for the person who started the run.</param>
public sealed record RunContentRefusal(string FailureKind, string FailureDetail);
