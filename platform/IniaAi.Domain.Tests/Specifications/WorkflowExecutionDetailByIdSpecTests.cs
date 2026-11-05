using IniaAi.Domain.Specifications.Workflows;

namespace IniaAi.Domain.Tests.Specifications;

/// <summary>
/// The spec eagerly loads several sibling collection navigation properties at the workflow root and
/// several more under each block; in one SQL statement those multiply into a cartesian row explosion
/// (EF's MultipleCollectionInclude warning). Every duplicated row carries the workflow's
/// Configuration and ExecutionState jsonb, which streamed gigabytes into the API heap until it threw
/// OutOfMemoryException, so it must stay a split query.
/// </summary>
public class WorkflowExecutionDetailByIdSpecTests
{
    [Fact]
    public void SplitsCollectionIncludesAcrossQueries()
    {
        Assert.True(new WorkflowExecutionDetailByIdSpec().AsSplitQuery);
    }
}
