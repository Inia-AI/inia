namespace IniaAi.Configuration.Platform;

public class EnvironmentBannerSettings
{
    /// <summary>
    /// Forces the environment indicator on (<c>true</c>) or off (<c>false</c>). When left
    /// <c>null</c>, the indicator shows for every non-Production environment; Production stays
    /// unmarked unless a <see cref="Label"/> or <see cref="Color"/> is supplied (either counts
    /// as intent to show). A stock deployment (which reports Production and sets neither) stays
    /// unmarked with no configuration.
    /// </summary>
    public bool? Enabled { get; set; }

    public string? Label { get; set; }

    public string? Color { get; set; }
}
