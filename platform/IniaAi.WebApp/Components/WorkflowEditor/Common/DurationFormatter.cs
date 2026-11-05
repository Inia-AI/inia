namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class DurationFormatter
{
    public static string Format(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.TotalMinutes:F1}m";
        }

        if (duration.TotalSeconds >= 1)
        {
            return $"{duration.TotalSeconds:F1}s";
        }

        return $"{duration.TotalMilliseconds:F0}ms";
    }
}
