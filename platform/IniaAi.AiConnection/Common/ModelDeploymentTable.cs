using System.Diagnostics.CodeAnalysis;

namespace IniaAi.AiConnection.Common;

/// <summary>
/// The deployment names a provider identifies its models by, keyed on the exact configured
/// name. Immutable: <see cref="With"/> returns a new table, so a snapshot handed to a caller
/// never changes underneath it. A name belongs to one provider; registering it for a second
/// is refused, as is a name the provider's own shape rule rejects.
/// </summary>
public sealed class ModelDeploymentTable
{
    public static readonly ModelDeploymentTable Empty = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private readonly Dictionary<string, string> _providerByName;

    private ModelDeploymentTable(Dictionary<string, string> providerByName)
    {
        _providerByName = providerByName;
    }

    public int Count => _providerByName.Count;

    public ModelDeploymentTable With(string provider, IEnumerable<string> deploymentNames)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("A deployment table row needs a provider.", nameof(provider));
        }

        var next = new Dictionary<string, string>(_providerByName, StringComparer.Ordinal);
        foreach (string name in deploymentNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"A {provider} deployment name cannot be blank.", nameof(deploymentNames));
            }

            if (ModelProviders.DescribeModelIdRefusal(provider, name) is string refusal)
            {
                throw new ArgumentException(refusal, nameof(deploymentNames));
            }

            if (next.TryGetValue(name, out string? owner) && !string.Equals(owner, provider, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Deployment name '{name}' is already registered for provider '{owner}' and cannot also "
                    + $"name a {provider} deployment.",
                    nameof(deploymentNames));
            }

            next[name] = provider;
        }

        return new ModelDeploymentTable(next);
    }

    public bool TryGetProvider(string? modelId, [NotNullWhen(true)] out string? provider)
    {
        provider = null;
        return modelId is not null && _providerByName.TryGetValue(modelId, out provider);
    }
}
