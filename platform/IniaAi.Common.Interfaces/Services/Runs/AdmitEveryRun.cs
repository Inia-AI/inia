using ErrorOr;

namespace IniaAi.Common.Interfaces.Services.Runs;

public sealed class AdmitEveryRun : IRunAdmission
{
    public Task<ErrorOr<Success>> AdmitAsync(RunAdmissionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}
