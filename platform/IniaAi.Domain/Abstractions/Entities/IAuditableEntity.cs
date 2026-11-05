namespace IniaAi.Domain.Abstractions.Entities;

public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedByUserId { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedByUserId { get; set; }
    bool IsDeleted { get; set; }
}
