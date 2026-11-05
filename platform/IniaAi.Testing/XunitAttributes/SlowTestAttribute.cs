namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// Lets a test, or every test of a class, take longer than <see cref="TestTimeCeilingAttribute.Ceiling"/>
/// on CI. The reason says what the time buys, so a reviewer can judge whether the test should be
/// made faster instead.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SlowTestAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
