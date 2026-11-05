using System.Reflection;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetWorkflowExecution;

/// <summary>
/// Holds the classic activity surface's structural guarantee: the wire shape is an allowlist
/// with no dedicated field for block names, section titles, ids, types, prompts, or counts of
/// internal steps — a property added for any of those fails here before a serializer runs.
/// What a Label string contains is the writer's responsibility (prompt rules plus
/// AcceptLabel's refusal and machinery-syntax filters), not this test's.
/// </summary>
public class WorkflowActivityDtoContractTests
{
    private static readonly Dictionary<string, string[]> _allowedProperties = new()
    {
        [nameof(WorkflowActivityDto)] =
        [
            nameof(WorkflowActivityDto.SchemaVersion),
            nameof(WorkflowActivityDto.CurrentStepIndex),
            nameof(WorkflowActivityDto.Steps),
        ],
        [nameof(WorkflowActivityStepDto)] =
        [
            nameof(WorkflowActivityStepDto.Label),
            nameof(WorkflowActivityStepDto.Status),
        ],
    };

    [Fact]
    public void TheWireShapeCarriesExactlyTheAllowedProperties()
    {
        foreach ((Type type, string[] allowed) in new[]
        {
            (typeof(WorkflowActivityDto), _allowedProperties[nameof(WorkflowActivityDto)]),
            (typeof(WorkflowActivityStepDto), _allowedProperties[nameof(WorkflowActivityStepDto)]),
        })
        {
            string[] actual = [.. type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)];

            Assert.Equal(allowed.OrderBy(name => name, StringComparer.Ordinal), actual);
        }
    }

    [Fact]
    public void EveryLeafIsAPlainLabelOrAnIndex()
    {
        Assert.Equal(typeof(int), typeof(WorkflowActivityDto).GetProperty(nameof(WorkflowActivityDto.SchemaVersion))!.PropertyType);
        Assert.Equal(typeof(int), typeof(WorkflowActivityDto).GetProperty(nameof(WorkflowActivityDto.CurrentStepIndex))!.PropertyType);
        Assert.Equal(
            typeof(List<WorkflowActivityStepDto>),
            typeof(WorkflowActivityDto).GetProperty(nameof(WorkflowActivityDto.Steps))!.PropertyType);
        Assert.Equal(typeof(string), typeof(WorkflowActivityStepDto).GetProperty(nameof(WorkflowActivityStepDto.Label))!.PropertyType);
    }
}
