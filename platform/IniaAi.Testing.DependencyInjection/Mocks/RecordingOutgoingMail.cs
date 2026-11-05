using IniaAi.Application.Common.Interfaces.Services;

namespace IniaAi.Testing.DependencyInjection.Mocks;

public sealed record SentEmail(string To, string Subject, string HtmlBody);

/// <summary>
/// Outgoing mail that keeps each message instead of sending it, switched on or off the way a
/// deployment's mail settings would.
/// </summary>
public sealed class RecordingOutgoingMail(bool isConfigured) : IOutgoingMail
{
    public List<SentEmail> Sent { get; } = [];

    public bool IsConfigured { get; } = isConfigured;

    public Task SendAsync(OutgoingMailMessage message)
    {
        Sent.Add(new SentEmail(message.To, message.Subject, message.HtmlBody));
        return Task.CompletedTask;
    }
}
