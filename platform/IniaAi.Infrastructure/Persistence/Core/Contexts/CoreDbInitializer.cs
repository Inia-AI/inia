using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Tools.GraphRag;
using IniaAi.Domain.Tools.PdfOcr;
using IniaAi.Domain.Tools.RagStrategies;
using IniaAi.Domain.Tools.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.Core.Contexts;

public class CoreDbInitializer
{
    private readonly CoreDbContext _context;
    private readonly Dictionary<string, bool> _tableStatuses;
    private readonly IReadOnlyList<ICorePromptsSeed> _modulePromptSeeds;
    private readonly ModuleWorkflowTypes _moduleWorkflowTypes;

    public CoreDbInitializer(
        CoreDbContext context,
        IEnumerable<ICorePromptsSeed>? modulePromptSeeds = null,
        ModuleWorkflowTypes? moduleWorkflowTypes = null)
    {
        _context = context;
        _tableStatuses = context.GetTableStatuses();
        _modulePromptSeeds = [.. (modulePromptSeeds ?? []).DistinctBy(seed => seed.GetType())];
        _moduleWorkflowTypes = moduleWorkflowTypes ?? new ModuleWorkflowTypes([]);
    }

    // Default organization IDs used for seeding
    private const string DEFAULT_ORGANIZATION_ID = "IniaAI";
    private const string TEST_CATEGORY_ID = "test-category";
    private const string MONITORING_CATEGORY_ID = "monitoring-category";

    public void Initialize()
    {
        SeedDocumentCategories();

        SeedWorkflowTypeCategories();

        SeedSharedComponentCategories();

        SeedWorkflowTypes();

        SeedWorkflowTypeOrganizationAccess();

        SeedWorkflows();

        SeedToolsSettings();

        _ = _context.SaveChanges();
    }

    private void SeedToolsSettings()
    {
        if (!_tableStatuses[nameof(_context.UseAutoDetectedTableOfContentsPrompts)]
            || !_context.UseAutoDetectedTableOfContentsPrompts.Any(p => p.CreatedAt == UseAutoDetectedTableOfContentsPrompts.PromptsUpdateDate))
        {
            var useAutoDetectedTableOfContentsPrompts = new UseAutoDetectedTableOfContentsPrompts()
            {
                CreatedAt = UseAutoDetectedTableOfContentsPrompts.PromptsUpdateDate,
            };

            _ = _context.Add(useAutoDetectedTableOfContentsPrompts);
        }

        if (!_tableStatuses[nameof(_context.PdfOcrPrompts)]
            || !_context.PdfOcrPrompts.Any(p => p.CreatedAt == PdfOcrPrompts.PromptsUpdateDate))
        {
            var pdfOcrPrompts = new PdfOcrPrompts()
            {
                CreatedAt = PdfOcrPrompts.PromptsUpdateDate,
            };

            _ = _context.Add(pdfOcrPrompts);
        }

        if (!_tableStatuses[nameof(_context.ReconciliationPrompts)]
            || !_context.ReconciliationPrompts.Any(p => p.CreatedAt == ReconciliationPrompts.PromptsUpdateDate))
        {
            var reconciliationPrompts = new ReconciliationPrompts()
            {
                CreatedAt = ReconciliationPrompts.PromptsUpdateDate,
            };

            _ = _context.Add(reconciliationPrompts);
        }

        if (!_tableStatuses[nameof(_context.GraphRagPrompts)]
            || !_context.GraphRagPrompts.Any(p => p.CreatedAt == GraphRagPrompts.PromptsUpdateDate))
        {
            var graphRagPrompts = new GraphRagPrompts()
            {
                CreatedAt = GraphRagPrompts.PromptsUpdateDate,
            };

            _ = _context.Add(graphRagPrompts);
        }

        _ = _context.SaveChanges();

        foreach (ICorePromptsSeed seed in _modulePromptSeeds)
        {
            seed.Seed(_context, _tableStatuses);
        }

        _ = _context.SaveChanges();
    }

    private void SeedWorkflows()
    {
        if (_tableStatuses[nameof(_context.Workflows)])
        {
            return;
        }

        Workflow testWorkflow = CreateGenericWorkflow(
            "test-queries",
            "Test Workflow",
            WorkflowType.TEST_WORKFLOW_TYPE);

        _context.AddRange(_moduleWorkflowTypes.All.Select(type => type.CreateSeedWorkflow()));
        _ = _context.Add(testWorkflow);

        _ = _context.SaveChanges();
    }

    private void SeedDocumentCategories()
    {
        if (_tableStatuses.TryGetValue(nameof(_context.DocumentCategories), out bool hasData) && hasData)
        {
            return;
        }

        if (_context.DocumentCategories.IgnoreQueryFilters().Any())
        {
            return;
        }

        var documentCategories = new List<DocumentCategory>
        {
            new() {
                Id = "company-report",
                Name = "Company Report",
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new() {
                Id = "legal-agreement",
                Name = "Legal Agreement",
                DisplayOrder = 2,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new() {
                Id = "presentation",
                Name = "Presentation",
                DisplayOrder = 3,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new() {
                Id = "financial-model",
                Name = "Financial Model",
                DisplayOrder = 4,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        _context.AddRange(documentCategories);

        _ = _context.SaveChanges();
    }

    private void SeedWorkflowTypeCategories()
    {
        // Always ensure default categories exist, even if table has data
        var existingCategoryIds = _context.WorkflowTypeCategories
            .IgnoreQueryFilters()
            .Select(c => c.Id)
            .ToHashSet();

        var categoriesToAdd = new List<WorkflowTypeCategory>();

        if (!existingCategoryIds.Contains(MONITORING_CATEGORY_ID))
        {
            categoriesToAdd.Add(new WorkflowTypeCategory
            {
                Id = MONITORING_CATEGORY_ID,
                Name = "Monitoring Workflows",
                Description = "Workflows for monitoring and analysis",
                DisplayOrder = 1
            });
        }

        if (!existingCategoryIds.Contains(TEST_CATEGORY_ID))
        {
            categoriesToAdd.Add(new WorkflowTypeCategory
            {
                Id = TEST_CATEGORY_ID,
                Name = "Test Workflows",
                Description = "Workflows for testing and development",
                DisplayOrder = 999
            });
        }

        if (categoriesToAdd.Count > 0)
        {
            _context.AddRange(categoriesToAdd);
            _ = _context.SaveChanges();
        }
    }

    private void SeedSharedComponentCategories()
    {
        var seedCategories = new List<SharedComponentCategory>
        {
            new() { Id = "load-verify", Name = "Load & Verify", Description = "Document loading and verification steps", DisplayOrder = 1 },
            new() { Id = "extraction", Name = "Extraction", Description = "Data and content extraction from documents", DisplayOrder = 2 },
            new() { Id = "preparation", Name = "Preparation", Description = "Baseline, indexing and other preparatory steps", DisplayOrder = 3 },
            new() { Id = "analysis", Name = "Analysis", Description = "Analytical assessments over extracted data", DisplayOrder = 4 },
            new() { Id = "research-search", Name = "Research & Search", Description = "Web, news and deep-search research", DisplayOrder = 5 },
            new() { Id = "output-composition", Name = "Output & Composition", Description = "Writeups, results and final composition", DisplayOrder = 6 },
            new() { Id = "utilities", Name = "Utilities", Description = "Cross-cutting helpers such as rates, dates and indexing", DisplayOrder = 7 },
        };

        var existingCategoryIds = _context.SharedComponentCategories
            .IgnoreQueryFilters()
            .Select(c => c.Id)
            .ToHashSet();
        List<SharedComponentCategory> categoriesToAdd = [.. seedCategories.Where(c => !existingCategoryIds.Contains(c.Id))];

        if (categoriesToAdd.Count > 0)
        {
            _context.AddRange(categoriesToAdd);
            _ = _context.SaveChanges();
        }
    }

    private void SeedWorkflowTypes()
    {
        // Define the expected workflow types with their categories
        var expectedWorkflowTypes = new Dictionary<string, (string Name, string CategoryId)>
        {
            { WorkflowType.GENERIC, ("Generic", MONITORING_CATEGORY_ID) },
            { WorkflowType.TEST_WORKFLOW_TYPE, ("Test Workflow Type", TEST_CATEGORY_ID) }
        };

        foreach (ModuleWorkflowType moduleType in _moduleWorkflowTypes.All)
        {
            expectedWorkflowTypes[moduleType.Id] = (moduleType.Name, MONITORING_CATEGORY_ID);
        }

        // Get existing workflow types
        var existingTypes = _context.WorkflowTypes
            .Where(wt => expectedWorkflowTypes.Keys.Contains(wt.Id))
            .ToList();

        var existingIds = existingTypes.Select(wt => wt.Id).ToHashSet();

        // Add missing workflow types
        var typesToAdd = expectedWorkflowTypes
            .Where(kvp => !existingIds.Contains(kvp.Key))
            .Select(kvp => new WorkflowType(kvp.Key, kvp.Value.Name) { CategoryId = kvp.Value.CategoryId })
            .ToList();

        if (typesToAdd.Count > 0)
        {
            _context.AddRange(typesToAdd);
        }

        // Update existing workflow types that don't have a CategoryId
        foreach (WorkflowType? wt in existingTypes)
        {
            if (string.IsNullOrEmpty(wt.CategoryId) && expectedWorkflowTypes.TryGetValue(wt.Id, out (string Name, string CategoryId) expected))
            {
                wt.CategoryId = expected.CategoryId;
            }
        }

        _ = _context.SaveChanges();
    }

    private void SeedWorkflowTypeOrganizationAccess()
    {
        // Define expected access records (workflow type -> organization)
        (string, string DEFAULT_ORGANIZATION_ID)[] expectedAccess =
        [
            (WorkflowType.GENERIC, DEFAULT_ORGANIZATION_ID),
            .. _moduleWorkflowTypes.All.Select(type => (type.Id, DEFAULT_ORGANIZATION_ID)),
            (WorkflowType.TEST_WORKFLOW_TYPE, DEFAULT_ORGANIZATION_ID)
        ];

        // Get existing access records
        var existingAccess = _context.WorkflowTypeOrganizationAccess
            .IgnoreQueryFilters()
            .Where(a => a.OrganizationId == DEFAULT_ORGANIZATION_ID)
            .Select(a => a.WorkflowTypeId)
            .ToHashSet();

        // Add missing access records
        var accessToAdd = expectedAccess
            .Where(e => !existingAccess.Contains(e.Item1))
            .Select(e => new WorkflowTypeOrganizationAccess
            {
                Id = Guid.NewGuid().ToString(),
                WorkflowTypeId = e.Item1,
                OrganizationId = e.DEFAULT_ORGANIZATION_ID
            })
            .ToList();

        if (accessToAdd.Count > 0)
        {
            _context.AddRange(accessToAdd);
            _ = _context.SaveChanges();
        }
    }

    private static Workflow CreateGenericWorkflow(
        string name,
        string title,
        string type)
    {
        var rootWorkflow = new Workflow(name)
        {
            Title = title,
            Description = title,
            WorkflowTypeId = type,
            Configuration = new WorkflowConfiguration { PageOverviews = PageOverviewPromptDefaults.Create() },
        };

        var section1Setup = new Section($"{name}-section-1-setup", rootWorkflow)
        {
            Title = "Section 1 Setup",
            Description = "Section 1 Setup",
        };
        _ = new Block(BlockTypes.Merge, section1Setup)
        {
            Name = "Section 1 Setup Block 1",
            ReplacementTag = "section1SetupBlock1",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>Paragraph 1 of Section 1</answer>",
            },
        };
        _ = new Block(BlockTypes.Merge, section1Setup)
        {
            Name = "Section 1 Setup Block 2",
            ReplacementTag = "section1SetupBlock2",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>Paragraph 2 of Section 1</answer>",
            },
        };

        var section2Setup = new Section($"{name}-section-2-setup", rootWorkflow)
        {
            Title = "Section 2 Setup",
            Description = "Section 2 Setup",
        };
        _ = new Block(BlockTypes.Merge, section2Setup)
        {
            Name = "Section 2 Setup Block 1",
            ReplacementTag = "section2SetupBlock1",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>Paragraph 1 of Section 2</answer>",
            },
        };
        _ = new Block(BlockTypes.Merge, section2Setup)
        {
            Name = "Section 2 Setup Block 2",
            ReplacementTag = "section2SetupBlock2",
            Properties = new MergeProperties
            {
                TextTemplate = "<answer>Paragraph 2 of Section 2</answer>",
            },
        };

        var section3Setup = new Section($"{name}-section-3-setup", rootWorkflow)
        {
            Title = "Section 3 Setup",
            Description = "Section 3 Setup",
        };
        _ = new Block(BlockTypes.AiQuery, section3Setup)
        {
            Name = "Section 3 Setup Block 1",
            ReplacementTag = "section3SetupBlock1",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Respond only with \"YES\"",
                SystemPromptTemplate = "Strictly follow the instructions",
            }
        };
        var section3SetupBlock2 = new Block(BlockTypes.SimpleRag, section3Setup)
        {
            Name = "Section 3 Setup Block 2",
            ReplacementTag = "section3SetupBlock2",
            SupportedDocumentClasses = ["class1"],
        };
        _ = new RagSettings(section3SetupBlock2)
        {
            Type = RagTypes.WholeDocument,
        };
        var section3SetupBlock3 = new Block(BlockTypes.SimpleRag, section3Setup)
        {
            Name = "Section 3 Setup Block 3",
            ReplacementTag = "section3SetupBlock3",
            SupportedDocumentClasses = ["class2"],
        };
        _ = new RagSettings(section3SetupBlock3)
        {
            Type = RagTypes.WholeDocument,
        };

        return rootWorkflow;
    }
}
