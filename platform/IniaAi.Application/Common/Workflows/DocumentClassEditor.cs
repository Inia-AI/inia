using ErrorOr;
using IniaAi.Common;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.Workflows;

public class DocumentClassEditor : IDocumentClassEditor
{
    public ErrorOr<DocumentClass> Upsert(Workflow workflow, DocumentClassEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.Key))
        {
            return Error.Validation("DocumentClass.Key", "Key is required.");
        }

        if (string.IsNullOrWhiteSpace(edit.DisplayName))
        {
            return Error.Validation("DocumentClass.DisplayName", "Display name is required.");
        }

        if (edit.Key is DocumentClass.KEY_ANY or DocumentClass.KEY_NONE)
        {
            return Error.Validation(
                "DocumentClass.Key",
                $"'{edit.Key}' is a reserved value and cannot be used as a document class key.");
        }

        DocumentClass? existing = null;
        if (!string.IsNullOrWhiteSpace(edit.OriginalKey))
        {
            existing = workflow.DocumentClasses.FirstOrDefault(
                dc => dc.Key.Equals(edit.OriginalKey, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return Error.NotFound(
                    "DocumentClass.NotFound",
                    $"Document class '{edit.OriginalKey}' was not found.");
            }
        }

        bool keyChanged = existing is null || !string.Equals(existing.Key, edit.Key, StringComparison.Ordinal);
        if (keyChanged
            && workflow.DocumentClasses.Any(dc =>
                !ReferenceEquals(dc, existing) && dc.Key.Equals(edit.Key, StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Conflict(
                "DocumentClass.DuplicateKey",
                $"A document class with key '{edit.Key}' already exists.");
        }

        DocumentClass target;
        if (existing is not null)
        {
            workflow.RenameDocumentClassKey(existing, edit.Key);
            target = existing;
        }
        else
        {
            target = new DocumentClass(workflow, edit.Key);
        }

        target.DisplayName = edit.DisplayName;
        target.Description = edit.Description;
        target.DocumentCategoryId = edit.DocumentCategoryId;
        target.Requirement = edit.Requirement;
        target.ShouldBeSingular = edit.ShouldBeSingular;
        target.IsOutputDocument = edit.IsOutputDocument;
        target.LlmDescription = edit.LlmDescription.NullIfWhiteSpace();
        if (edit.PageOverviews is not null)
        {
            target.PageOverviews = edit.PageOverviews.WithoutBlankPrompts();
        }

        return target;
    }

    public ErrorOr<Deleted> Remove(Workflow workflow, string key)
    {
        DocumentClass? target = workflow.DocumentClasses.FirstOrDefault(
            dc => dc.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return Error.NotFound("DocumentClass.NotFound", $"Document class '{key}' was not found.");
        }

        workflow.RemoveDocumentClass(target);
        return Result.Deleted;
    }
}
