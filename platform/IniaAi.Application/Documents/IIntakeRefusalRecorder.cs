namespace IniaAi.Application.Documents;

/// <summary>
/// Where <see cref="DocumentIntakeGate"/> reports a refusal, in the same call that refuses, so a
/// refusal and its trail cannot come apart. The platform's own recorder writes a log line;
/// a deployment that keeps an audit table registers a recorder that writes the row.
/// </summary>
public interface IIntakeRefusalRecorder
{
    Task RecordAsync(IntakeRefusal refusal, IntakeMouth mouth, CancellationToken cancellationToken);
}
