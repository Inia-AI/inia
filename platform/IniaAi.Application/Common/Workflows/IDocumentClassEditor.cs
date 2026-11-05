using ErrorOr;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.Workflows;

public interface IDocumentClassEditor
{
    ErrorOr<DocumentClass> Upsert(Workflow workflow, DocumentClassEdit edit);

    ErrorOr<Deleted> Remove(Workflow workflow, string key);
}
