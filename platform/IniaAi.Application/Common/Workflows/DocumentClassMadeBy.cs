namespace IniaAi.Application.Common.Workflows;

/// <summary>The workflow type and output class that make an input class; <see cref="None"/> clears the link.</summary>
public sealed record DocumentClassMadeBy(string? WorkflowTypeId, string? DocumentClassKey)
{
    public static DocumentClassMadeBy None { get; } = new(null, null);
}
