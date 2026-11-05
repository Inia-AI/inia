using IniaAi.Configuration.Platform;

namespace IniaAi.Configuration.Tests.Platform;

/// <summary>
/// The default job count comes from container runs of the memory bench: under a 4 GiB limit
/// memory did not limit 3, 6 or 9 jobs, and 6 cleared a burst of emails faster than 3.
/// </summary>
public class IniaAiSettingsTests
{
    private const int MEASURED_JOB_COUNT = 6;

    [Fact]
    public void MaxConcurrentJobs_DefaultsToTheMeasuredJobCount()
    {
        Assert.Equal(MEASURED_JOB_COUNT, new IniaAiSettings().MaxConcurrentJobs);
    }
}
