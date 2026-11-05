using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using System.Linq.Expressions;
using ContractComponentType = IniaAi.WebApi.Contracts.Enums.PricingComponentType;
using TokenUsageEntity = IniaAi.Domain.Entities.TokenUsage.TokenUsage;

namespace IniaAi.Application.Queries.TokenUsage;

public static class TokenUsageProjections
{
    /// <summary>
    /// The user data model does not map workflow types, so a token usage row cannot reach a
    /// workflow type's name and <see cref="TokenUsageDto.WorkflowTypeName"/> stays empty.
    /// </summary>
    public static readonly Expression<Func<TokenUsageEntity, TokenUsageDto>> ToDto = usage => new TokenUsageDto
    {
        Id = usage.Id,
        OrganizationId = usage.OrganizationId,
        UserId = usage.UserId,
        UserDisplayName = usage.User != null ? (usage.User.FirstName + " " + usage.User.LastName).Trim() : null,
        WorkflowTypeId = usage.WorkflowTypeId,
        WorkflowExecutionId = usage.WorkflowExecutionId,
        BlockId = usage.BlockId,
        DocumentId = usage.DocumentId,
        UsageType = usage.UsageType.ToString(),
        ExecutionContext = usage.ExecutionContext.ToString(),
        BlockType = usage.BlockType != null ? usage.BlockType.ToString() : null,
        Provider = usage.Provider,
        ModelId = usage.ModelId,
        Components = usage.Components.Select(component => new TokenUsageComponentDto
        {
            ComponentType = (ContractComponentType)component.ComponentType,
            Units = component.Units,
            Cost = component.Cost,
        }).ToList(),
        TotalCost = usage.Components.Sum(component => component.Cost),
        CreatedAt = usage.CreatedAt,
        Description = usage.Description,
    };
}
