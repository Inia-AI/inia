using IniaAi.Application.Common.Interfaces.Services;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// A mail provider that keeps each message the platform's <c>OutgoingMail</c> hands it, so a test
/// reads the mail as it would leave, or fails every send the way a provider that is down fails.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly Lock _gate = new();
    private readonly List<OutgoingMailMessage> _sent = [];

    /// <summary>When set, every send throws it.</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, every send takes the message and never answers, as a mail server that hangs.</summary>
    public bool Hangs { get; set; }

    public IReadOnlyList<OutgoingMailMessage> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    public Task SendEmailAsync(OutgoingMailMessage message)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        if (Hangs)
        {
            return new TaskCompletionSource().Task;
        }

        lock (_gate)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }
}
