namespace IniaAi.AiConnection.Common.ChatConnector;

/// <summary>
/// A block failed because the model refused it. The message is what the run shows the reader:
/// that the block has no output and why. No retry and no other model is tried.
/// </summary>
public sealed class ModelRefusalException(ModelRefusal refusal)
    : InvalidOperationException(DescribeForReader(refusal))
{
    public ModelRefusal Refusal { get; } = refusal;

    public static string DescribeForReader(ModelRefusal refusal)
    {
        string reason = refusal.Category is ModelRefusal.WRITTEN_CATEGORY
            ? "it wrote a refusal in place of an answer"
            : $"refusal category: {refusal.Category}";

        return $"The model refused to answer this block ({reason}), so the block has no output.";
    }
}
