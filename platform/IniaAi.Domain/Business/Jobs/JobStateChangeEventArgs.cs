using IniaAi.Services.Models.BackgroundJobs;
using IniaAi.Services.Models.Jobs.Enums;

namespace IniaAi.Services.Models.Jobs;

public class JobStateChangeEventArgs : EventArgs
{
    public BackgroundJob Job { get; init; }

    public JobState FromState { get; init; }

    public JobState ToState { get; init; }

    public JobStateChangeEventArgs(BackgroundJob job, JobState fromState, JobState toState)
    {
        Job = job;
        FromState = fromState;
        ToState = toState;
    }
}
