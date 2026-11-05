using System.Reflection;
using IniaAi.Application.Queries.AgentRuns.GetAgentRunActivity;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;
using ModelActivity = IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

namespace IniaAi.Application.UnitTests.Queries.AgentRuns.GetAgentRunActivity;

/// <summary>
/// Holds the digest's core guarantee at the schema level: the wire shape cannot represent
/// internal machinery. A property whose name or type could carry a path, an identifier, a
/// command, a token count, or a cost fails here before any serializer runs.
/// </summary>
public class AgentActivityDigestDtoContractTests
{
    private static readonly string[] _forbiddenNameFragments =
    [
        "Path",
        "Command",
        "Token",
        "Cost",
        "Session",
        "Sha",
        "Uuid",
        "Prompt",
        "Model",
        "Usd",
    ];

    private static readonly Type[] _allowedLeafTypes =
    [
        typeof(string),
        typeof(int),
        typeof(int?),
        typeof(DateTimeOffset?),
        typeof(IniaAi.WebApi.Contracts.Enums.AgentActivityOutcome),
        typeof(IniaAi.WebApi.Contracts.Enums.AgentActivityBeatKind),
    ];

    private static IEnumerable<(string Owner, PropertyInfo Property)> WalkDigestGraph()
    {
        HashSet<Type> visited = [];
        Queue<Type> pending = new([typeof(AgentActivityDigestDto)]);
        while (pending.Count > 0)
        {
            Type current = pending.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (PropertyInfo property in current.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                yield return (current.Name, property);
                Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    propertyType = propertyType.GetGenericArguments()[0];
                }

                if (propertyType.Namespace == typeof(AgentActivityDigestDto).Namespace)
                {
                    pending.Enqueue(propertyType);
                }
            }
        }
    }

    [Fact]
    public void NoPropertyNameCanCarryInternalMachinery()
    {
        foreach ((string owner, PropertyInfo property) in WalkDigestGraph())
        {
            foreach (string fragment in _forbiddenNameFragments)
            {
                Assert.False(
                    property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"{owner}.{property.Name} carries forbidden fragment '{fragment}'");
            }

            Assert.False(
                property.Name.EndsWith("Id", StringComparison.Ordinal),
                $"{owner}.{property.Name} looks like an identifier");
        }
    }

    [Fact]
    public void EveryPropertyTypeComesFromTheClosedSet()
    {
        foreach ((string owner, PropertyInfo property) in WalkDigestGraph())
        {
            Type type = property.PropertyType;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                type = type.GetGenericArguments()[0];
            }

            bool allowed = _allowedLeafTypes.Contains(type)
                || type.Namespace == typeof(AgentActivityDigestDto).Namespace;
            Assert.True(allowed, $"{owner}.{property.Name} has unexpected type {type}");
        }
    }

    [Fact]
    public void EnumMirrorsStayInLockstepWithTheProjectionModel()
    {
        Assert.Equal(
            Enum.GetNames<ModelActivity.AgentActivityOutcome>(),
            Enum.GetNames<IniaAi.WebApi.Contracts.Enums.AgentActivityOutcome>());
        Assert.Equal(
            Enum.GetNames<ModelActivity.AgentActivityBeatKind>(),
            Enum.GetNames<IniaAi.WebApi.Contracts.Enums.AgentActivityBeatKind>());
    }

    [Fact]
    public void MapperCarriesEveryModelValueByName()
    {
        var digest = new ModelActivity.AgentActivityDigest
        {
            Outcome = ModelActivity.AgentActivityOutcome.Failed,
            StartedAtUtc = DateTimeOffset.Parse("2026-08-13T13:38:58.532Z"),
            Phases = [new ModelActivity.AgentActivityPhase("Reading", "Reading…")],
            Beats =
            [
                new ModelActivity.AgentActivityBeat(
                    12, 0, ModelActivity.AgentActivityBeatKind.Hero, "Viewed a page", "Page 3"),
            ],
            Receipt = new ModelActivity.AgentActivityReceipt
            {
                DurationSeconds = 90,
                DocumentNames = ["A.pdf"],
                SkillNames = ["skill-a"],
                PagesViewed = 1,
                CodeRuns = 2,
                ChecksReported = 3,
            },
        };

        AgentActivityDigestDto dto = AgentActivityDtoMapper.ToDto(digest);

        Assert.Equal(IniaAi.WebApi.Contracts.Enums.AgentActivityOutcome.Failed, dto.Outcome);
        Assert.Equal(DateTimeOffset.Parse("2026-08-13T13:38:58.532Z"), dto.StartedAtUtc);
        AgentActivityBeatDto beat = Assert.Single(dto.Beats);
        Assert.Equal(IniaAi.WebApi.Contracts.Enums.AgentActivityBeatKind.Hero, beat.Kind);
        Assert.Equal(12, beat.AtSeconds);
        Assert.Equal("Viewed a page", beat.Label);
        Assert.Equal("Page 3", beat.Detail);
        Assert.Equal(90, dto.Receipt.DurationSeconds);
        Assert.Equal(["A.pdf"], dto.Receipt.DocumentNames);
        Assert.Equal(["skill-a"], dto.Receipt.SkillNames);
        Assert.Equal(3, dto.Receipt.ChecksReported);
    }
}
