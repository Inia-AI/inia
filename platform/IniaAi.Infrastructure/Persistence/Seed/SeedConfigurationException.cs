namespace IniaAi.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeding needs a setting the deployment did not supply. Startup stops on it, unlike other
/// seeding failures, which the next boot retries.
/// </summary>
public sealed class SeedConfigurationException(string message) : InvalidOperationException(message);
