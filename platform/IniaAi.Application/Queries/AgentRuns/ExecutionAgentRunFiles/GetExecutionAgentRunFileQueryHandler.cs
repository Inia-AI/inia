using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.WorkflowExecutions;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;

public class GetExecutionAgentRunFileQueryHandler
    : IRequestHandler<GetExecutionAgentRunFileQuery, ErrorOr<AgentRunFileContent>>
{
    private const string FILE_NOT_FOUND_MESSAGE = "No collected agent run file matches this block and path.";
    private const char PATH_SEPARATOR = '/';
    private const char FILE_NAME_SEPARATOR = '_';

    private readonly IUserDataUnitOfWork _unitOfWork;

    public GetExecutionAgentRunFileQueryHandler(IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<AgentRunFileContent>> Handle(
        GetExecutionAgentRunFileQuery request,
        CancellationToken cancellationToken)
    {
        ErrorOr<WorkflowExecution> execution = await RunEvidenceAccess.ResolveAsync(
            _unitOfWork,
            request.OrganizationId,
            request.UserId,
            request.ExecutionId,
            request.CallerReadsEveryRun,
            cancellationToken);

        if (execution.IsError)
        {
            return execution.Errors;
        }

        if (string.IsNullOrWhiteSpace(execution.Value.WorkflowId))
        {
            return Error.NotFound(description: FILE_NOT_FOUND_MESSAGE);
        }

        IQueryable<AgentRunTraceFile> candidates = ExecutionAgentRunFiles
            .OfWorkflow(_unitOfWork.Repository<AgentRunTraceFile>().GetAll().AsNoTracking(), request.OrganizationId, execution.Value.WorkflowId)
            .Where(file => file.BlockId == request.BlockId && file.Path == request.Path);

        if (!string.IsNullOrWhiteSpace(request.RunExecutionId))
        {
            candidates = candidates.Where(file => file.RunExecutionId == request.RunExecutionId);
        }

        if (request.Attempt is int attempt)
        {
            candidates = candidates.Where(file => file.Attempt == attempt);
        }

        AgentRunTraceFile? file = await candidates
            .OrderByDescending(file => file.CollectedAt)
            .ThenByDescending(file => file.Attempt)
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null)
        {
            return Error.NotFound(description: FILE_NOT_FOUND_MESSAGE);
        }

        return new AgentRunFileContent
        {
            Content = file.Content,
            ContentType = ExecutionAgentRunFiles.ContentTypeOf(file.Path),
            FileName = $"{file.RunExecutionId}{FILE_NAME_SEPARATOR}{file.Attempt}{FILE_NAME_SEPARATOR}{file.Path.Replace(PATH_SEPARATOR, FILE_NAME_SEPARATOR)}",
        };
    }
}
