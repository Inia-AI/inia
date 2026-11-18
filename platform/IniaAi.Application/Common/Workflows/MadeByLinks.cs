using System.Linq.Expressions;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Workflows;

/// <summary>One class of a user-facing workflow type's current workflow, named for the editor.</summary>
public sealed record MadeByChoice(string WorkflowTypeId, string WorkflowTypeName, string DocumentClassKey, string DisplayName);

/// <summary>A current workflow that reads an output of another workflow type, with its input classes.</summary>
public sealed record MadeByReader(string WorkflowId, string WorkflowTypeId, string WorkflowTypeName, WorkflowConfiguration? Configuration, List<MadeByReaderInput> Inputs);

/// <summary>An input class of a <see cref="MadeByReader"/>, with the output its Made by link names, if any.</summary>
public sealed record MadeByReaderInput(string Key, string? MadeByWorkflowTypeId, string? MadeByDocumentClassKey)
{
    public bool IsMadeBy(string workflowTypeId)
    {
        return !string.IsNullOrWhiteSpace(MadeByDocumentClassKey) && string.Equals(MadeByWorkflowTypeId, workflowTypeId, StringComparison.Ordinal);
    }
}

/// <summary>
/// The Made by links between workflow types, read from each user-facing type's current workflow: the outputs an input
/// can be made by, and the inputs an output makes.
/// </summary>
public static class MadeByLinks
{
    /// <summary>The outputs of every user-facing type other than <paramref name="workflowTypeId"/>, ordered by type and output name.</summary>
    public static Task<List<MadeByChoice>> OutputsAsync(ICoreUnitOfWork core, string? workflowTypeId, CancellationToken cancellationToken)
    {
        return Choices(core, workflow => workflow.WorkflowTypeId != workflowTypeId, documentClass => documentClass.IsOutputDocument)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The output an input's link names, or null when the type's current workflow no longer makes it.</summary>
    public static Task<MadeByChoice?> OutputAsync(ICoreUnitOfWork core, string workflowTypeId, string outputClassKey, CancellationToken cancellationToken)
    {
        return Choices(core, workflow => workflow.WorkflowTypeId == workflowTypeId, documentClass => documentClass.IsOutputDocument && documentClass.Key == outputClassKey)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The inputs of user-facing types' current workflows that name this output as Made by.</summary>
    public static Task<List<MadeByChoice>> UsersOfAsync(ICoreUnitOfWork core, string workflowTypeId, string outputClassKey, CancellationToken cancellationToken)
    {
        return Choices(
                core,
                _ => true,
                documentClass => !documentClass.IsOutputDocument
                    && documentClass.MadeByWorkflowTypeId == workflowTypeId
                    && documentClass.MadeByDocumentClassKey == outputClassKey)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The current workflows of user-facing types among <paramref name="reachableTypeIds"/> that have an input made by
    /// one of these outputs of the workflow type, ordered by type name.
    /// </summary>
    public static Task<List<MadeByReader>> ReadersOfAsync(
        ICoreUnitOfWork core,
        string workflowTypeId,
        IReadOnlyCollection<string> outputClassKeys,
        IReadOnlyCollection<string> reachableTypeIds,
        CancellationToken cancellationToken)
    {
        return (from workflow in core.Repository<Workflow>().GetAll().CurrentOfEachType()
                join type in core.Repository<WorkflowType>().GetAll().Where(WorkflowType.UserFacing) on workflow.WorkflowTypeId equals type.Id
                where reachableTypeIds.Contains(type.Id)
                    && workflow.DocumentClasses.Any(documentClass => !documentClass.IsOutputDocument
                        && documentClass.MadeByWorkflowTypeId == workflowTypeId
                        && outputClassKeys.Contains(documentClass.MadeByDocumentClassKey))
                orderby type.Name, type.Id
                select new MadeByReader(
                    workflow.Id,
                    type.Id,
                    type.Name,
                    workflow.Configuration,
                    workflow.DocumentClasses
                        .Where(documentClass => !documentClass.IsOutputDocument)
                        .Select(documentClass => new MadeByReaderInput(documentClass.Key, documentClass.MadeByWorkflowTypeId, documentClass.MadeByDocumentClassKey))
                        .ToList()))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<MadeByChoice> Choices(
        ICoreUnitOfWork core,
        Expression<Func<Workflow, bool>> ofWorkflows,
        Expression<Func<DocumentClass, bool>> ofClasses)
    {
        return from workflow in core.Repository<Workflow>().GetAll().CurrentOfEachType().Where(ofWorkflows)
               join type in core.Repository<WorkflowType>().GetAll().Where(WorkflowType.UserFacing) on workflow.WorkflowTypeId equals type.Id
               from documentClass in workflow.DocumentClasses.AsQueryable().Where(ofClasses)
               orderby type.Name, documentClass.DisplayName
               select new MadeByChoice(type.Id, type.Name, documentClass.Key, documentClass.DisplayName);
    }
}
