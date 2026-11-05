using Ardalis.Specification;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Specifications.Documents;

/// <summary>
/// The newest live file stored under a name, for a lookup that has only the name to go on.
/// The name is compared in lower case, which the database providers translate. The lookup stays
/// inside the organizations the caller may read; <c>null</c> means every organization.
/// </summary>
public sealed class NewestFileMetadataByNameSpec : Specification<FileMetadata>
{
    public NewestFileMetadataByNameSpec(string lowerCaseName, IReadOnlyCollection<string>? organizationIds)
    {
        _ = Query
            .Where(f => !f.IsDeleted && f.Name.ToLower() == lowerCaseName)
            .OrderByDescending(f => f.CreatedAt)
            .AsNoTracking();

        if (organizationIds is not null)
        {
            _ = Query.Where(f => organizationIds.Contains(f.OrganizationId));
        }
    }
}
