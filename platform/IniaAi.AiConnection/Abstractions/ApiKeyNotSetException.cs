namespace IniaAi.AiConnection.Abstractions;

/// <summary>
/// A connector was called with no API key configured, so it sent nothing.
/// </summary>
public sealed class ApiKeyNotSetException(string message) : InvalidOperationException(message);
