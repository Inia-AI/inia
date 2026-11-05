using IniaAi.Services.Models.Jobs;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class AppStateJobAnnouncementTests
{
    private const string ORG = "org-a";
    private const string OTHER_ORG = "org-b";
    private const string USER = "user-a";
    private const string OTHER_USER = "user-b";

    [Fact]
    public void AnnouncesJobTo_TheUserWhoStartedTheRun_InItsOrganization()
    {
        Job run = Run(ORG, USER);

        Assert.True(AppState.AnnouncesJobTo(run, ORG, USER));
    }

    [Fact]
    public void AnnouncesJobTo_NeitherAnotherUserNorAnotherOrganization()
    {
        Job run = Run(ORG, USER);

        Assert.False(AppState.AnnouncesJobTo(run, ORG, OTHER_USER));
        Assert.False(AppState.AnnouncesJobTo(run, OTHER_ORG, USER));
        Assert.False(AppState.AnnouncesJobTo(run, ORG, userId: null));
    }

    [Fact]
    public void AnnouncesJobTo_NeverForADelegatedJob()
    {
        var turn = new DelegatedJob(ORG, USER, "Agent conversation message", static (_, _) => Task.CompletedTask);

        Assert.False(AppState.AnnouncesJobTo(turn, ORG, USER));
    }

    private static Job Run(string organizationId, string userId)
    {
        var job = new Job(organizationId, workflowTypeId: null);
        job.SetUserId(userId);
        return job;
    }
}
