namespace IniaAi.Domain.Reruns;

/// <summary>
/// Marks an entity whose rows are live business data that people and dashboards read, such as a
/// counterparty or a monitoring observation. A rerun never saves a change to one: see
/// <see cref="RerunWrites"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class LiveDataAttribute : Attribute
{
}
