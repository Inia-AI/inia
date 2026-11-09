namespace IniaAi.Core.Workflows;

/// <summary>
/// A block failure whose reason quotes the run's content, such as a formula with block outputs
/// filled in. <see cref="Reason"/> goes to the block's execution log and the run's failure detail,
/// where the reader sees it; <see cref="Exception.Message"/> stays fixed, so a log line that writes
/// the exception carries no content.
/// </summary>
public class BlockFailureException(string reason) : InvalidOperationException(LOGGED_MESSAGE)
{
    public const string LOGGED_MESSAGE = "The block failed on the values of this run; its execution log holds the reason.";

    public string Reason { get; } = reason;

    /// <summary>The reason a reader sees for a failure: the reason of a <see cref="BlockFailureException"/>, the message of any other.</summary>
    public static string ReasonOf(Exception exception)
    {
        return exception is BlockFailureException failure ? failure.Reason : exception.Message;
    }
}
