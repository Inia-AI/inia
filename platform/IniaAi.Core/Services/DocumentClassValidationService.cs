using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.DocumentClassValidation;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Services;

public class DocumentClassValidationService : IDocumentClassValidationService
{
    private readonly ILogger<DocumentClassValidationService> _logger;

    public DocumentClassValidationService(ILogger<DocumentClassValidationService> logger)
    {
        _logger = logger;
    }

    public DocumentClassValidationResult ValidateDocumentClasses(Workflow workflow, IEnumerable<Document> documents)
    {
        return ValidateDocumentClasses(workflow, documents.Select(document => document.Class));
    }

    public DocumentClassValidationResult ValidateDocumentClasses(Workflow workflow, IEnumerable<string?> documentClasses)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var statuses = new List<DocumentClassStatus>();

        List<DocumentClass> workflowClasses = workflow.DocumentClasses?.ToList() ?? [];

        // Backwards compatibility: if no DocumentClasses entities but blocks have SupportedDocumentClasses, create temporary ones
        if (workflowClasses.Count == 0)
        {
            var legacyClasses = workflow.GetAllBlocks()
                .SelectMany(b => b.SupportedDocumentClasses)
                .Distinct()
                .Where(c => c is not "Any" and not "None")
                .ToList();

            if (legacyClasses.Count > 0)
            {
                _logger.LogDebug("Using legacy block document classes for workflow {WorkflowId}", workflow.Id);
                workflowClasses = [.. legacyClasses.Select(className => new DocumentClass(workflow, className)
                {
                    DisplayName = className,
                    Requirement = DocumentClassRequirement.Optional,
                    ShouldBeSingular = false
                })];
            }
        }

        if (workflowClasses.Count == 0)
        {
            _logger.LogDebug("No document classes defined for workflow {WorkflowId}", workflow.Id);
            return new DocumentClassValidationResult
            {
                IsValid = true,
                HasWarnings = false,
                DocumentClassStatuses = statuses
            };
        }

        Dictionary<string, int> documentsByClass = documentClasses
            .Where(documentClass => !string.IsNullOrWhiteSpace(documentClass))
            .GroupBy(documentClass => documentClass!)
            .ToDictionary(group => group.Key, group => group.Count());

        // Validate each document class
        foreach (DocumentClass? documentClass in workflowClasses)
        {
            int uploadedCount = documentsByClass.GetValueOrDefault(documentClass.Key);

            DocumentClassStatus status = ValidateDocumentClass(documentClass, uploadedCount, errors, warnings);
            statuses.Add(status);
        }

        return new DocumentClassValidationResult
        {
            IsValid = errors.Count == 0,
            HasWarnings = warnings.Count > 0,
            Errors = errors,
            Warnings = warnings,
            DocumentClassStatuses = statuses
        };
    }

    private static DocumentClassStatus ValidateDocumentClass(
        DocumentClass documentClass,
        int uploadedCount,
        List<string> errors,
        List<string> warnings)
    {
        bool isSatisfied = true;
        string? validationMessage = null;

        // Use DisplayName if available, otherwise fall back to Key
        string displayName = !string.IsNullOrWhiteSpace(documentClass.DisplayName)
            ? documentClass.DisplayName
            : documentClass.Key;

        if (documentClass.IsOutputDocument && uploadedCount > 0)
        {
            // There cannot be a document uploaded for an output document class
            isSatisfied = false;
            validationMessage = $"Output document class '{displayName}' should not have uploaded documents";
            errors.Add(validationMessage);
            return new DocumentClassStatus
            {
                Key = documentClass.Key,
                DisplayName = documentClass.DisplayName,
                Requirement = documentClass.Requirement.ToString(),
                ShouldBeSingular = documentClass.ShouldBeSingular,
                UploadedCount = uploadedCount,
                IsSatisfied = isSatisfied,
                ValidationMessage = validationMessage,
            };
        }
        else if (documentClass.IsOutputDocument && uploadedCount == 0)
        {
            // Output document class with no uploaded documents is always satisfied
            return new DocumentClassStatus
            {
                Key = documentClass.Key,
                DisplayName = documentClass.DisplayName,
                Requirement = documentClass.Requirement.ToString(),
                ShouldBeSingular = documentClass.ShouldBeSingular,
                UploadedCount = uploadedCount,
                IsSatisfied = true,
                ValidationMessage = null,
            };
        }

        // Check requirement level
        switch (documentClass.Requirement)
        {
            case DocumentClassRequirement.Required:
                if (uploadedCount == 0)
                {
                    isSatisfied = false;
                    validationMessage = $"Required document class '{displayName}' is missing";
                    errors.Add(validationMessage);
                }

                break;

            case DocumentClassRequirement.Suggested:
                if (uploadedCount == 0)
                {
                    validationMessage = $"Suggested document class '{displayName}' is missing";
                    warnings.Add(validationMessage);
                }

                break;

            case DocumentClassRequirement.Optional:
                // No validation needed for optional
                break;
        }

        // Check singular constraint
        if (documentClass.ShouldBeSingular && uploadedCount > 1)
        {
            isSatisfied = false;
            validationMessage = $"Only one '{displayName}' document is allowed, but {uploadedCount} were uploaded";
            errors.Add(validationMessage);
        }

        return new DocumentClassStatus
        {
            Key = documentClass.Key,
            DisplayName = documentClass.DisplayName,
            Requirement = documentClass.Requirement.ToString(),
            ShouldBeSingular = documentClass.ShouldBeSingular,
            UploadedCount = uploadedCount,
            IsSatisfied = isSatisfied,
            ValidationMessage = validationMessage
        };
    }
}
