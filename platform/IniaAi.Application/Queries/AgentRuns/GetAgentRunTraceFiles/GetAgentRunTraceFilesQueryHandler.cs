using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Specifications.AgentRuntime;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Application.Queries.AgentRuns.GetAgentRunTraceFiles;

public class GetAgentRunTraceFilesQueryHandler
    : IRequestHandler<GetAgentRunTraceFilesQuery, ErrorOr<List<AgentRunTraceFile>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;

    public GetAgentRunTraceFilesQueryHandler(IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<List<AgentRunTraceFile>>> Handle(
        GetAgentRunTraceFilesQuery request,
        CancellationToken cancellationToken)
    {
        IList<AgentRunTraceFile> files = await _unitOfWork.Repository<AgentRunTraceFile>()
            .GetAllAsync(
                new AgentRunTraceFilesByBlockSpec(request.OrganizationId, request.BlockId),
                cancellationToken);

        return files.ToList();
    }
}
