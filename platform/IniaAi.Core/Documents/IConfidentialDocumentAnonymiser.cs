using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Models.Jobs;

namespace IniaAi.Core.Documents;

/// <summary>
/// Replaces the documents of a confidential run with anonymised copies before the run reads them.
/// Only the anonymisation module registers it; a build without the module refuses confidential
/// runs before they start.
/// </summary>
public interface IConfidentialDocumentAnonymiser
{
    Task AnonymiseAsync(Job job, IUserDataUnitOfWork userDataUnitOfWork, CancellationToken cancellationToken);
}
