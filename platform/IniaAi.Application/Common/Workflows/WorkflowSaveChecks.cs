using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Signatures;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.JsonSchemas;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Every check a template save runs before it writes, run on a candidate workflow without saving
/// it: the workflow JSON Schema, the structural validator, the Library references, agent template
/// tags nothing can fill (a save only warns about these, under <see cref="WorkflowCheck.AgentTags"/>), the agent and verification gates (with the screening guard beside them),
/// the rules of the closed modules on their own settings, and signature-field deletions. Unlike the validate endpoint it skips none of them, and it reports
/// every failure rather than stopping at the first, so whoever fixes the workflow sees them all.
/// </summary>
public sealed class WorkflowSaveChecks
{
    private static readonly Lazy<CompiledJsonSchema> _schema = new(() => JsonSchemaEvaluation.CompileSchema(WorkflowJsonSchema.Json));

    private readonly IWorkflowTemplateStructuralValidator _structuralValidator;
    private readonly WorkflowTemplateSavePreflight _preflight;
    private readonly ICoreDbContext _coreDbContext;
    private readonly WorkflowSaveGates _gates;
    private readonly ISuperAdminLookup _superAdmins;

    public WorkflowSaveChecks(
        IWorkflowTemplateStructuralValidator structuralValidator,
        WorkflowTemplateSavePreflight preflight,
        ICoreDbContext coreDbContext,
        IOptions<AgentRuntimeSettings> agentRuntimeSettings,
        ISuperAdminLookup superAdmins,
        IAgentRunLimitsProvider? runLimitsProvider = null,
        IOptions<WorkflowExecutionJanitorSettings>? janitorSettings = null,
        IOptions<PlatformSettings>? platformSettings = null)
    {
        _structuralValidator = structuralValidator;
        _preflight = preflight;
        _coreDbContext = coreDbContext;
        _superAdmins = superAdmins;
        _gates = new WorkflowSaveGates(
            agentRuntimeSettings.Value,
            runLimitsProvider,
            janitorSettings?.Value,
            platformSettings?.Value.MaxBlockParallelism,
            platformSettings?.Value.ModelRouting);
    }

    /// <param name="previous">The workflow the candidate replaces, for the signature-field deletion check and the checks that compare a setting with the stored one.</param>
    /// <param name="userId">The user who would save the candidate, for the checks that depend on the user's role.</param>
    public async Task<List<WorkflowCheckFailure>> CheckAsync(
        string workflowTypeId,
        string workflowJson,
        Workflow? previous,
        string userId,
        CancellationToken cancellationToken)
    {
        var failures = new List<WorkflowCheckFailure>();

        JToken instance;
        try
        {
            instance = JToken.Parse(workflowJson);
        }
        catch (JsonException exception)
        {
            failures.Add(new WorkflowCheckFailure(WorkflowCheck.Schema, $"The workflow is not valid JSON: {exception.Message}"));
            return failures;
        }

        failures.AddRange(JsonSchemaEvaluation.FindLeafViolations(_schema.Value, instance)
            .Select(static violation => new WorkflowCheckFailure(WorkflowCheck.Schema, violation.ToString())));

        WorkflowStructuralValidationResult structure = _structuralValidator.Validate(workflowJson);
        failures.AddRange(structure.Errors.Select(static error => new WorkflowCheckFailure(WorkflowCheck.Structure, Describe(error))));
        if (structure.Workflow is not Workflow workflow)
        {
            return failures;
        }

        failures.AddRange((await _preflight.ValidateSharedComponentReferencesAsync(workflow, cancellationToken))
            .Select(static error => new WorkflowCheckFailure(WorkflowCheck.Library, Describe(error))));

        failures.AddRange(AgentTemplateGrammar.FindUnknownTags(workflow).Select(static unknown => new WorkflowCheckFailure(
            WorkflowCheck.AgentTags,
            $"'{{{{{unknown.Tag}}}}}' in {unknown.Location.Description} of block '{unknown.BlockTag}' is a tag nothing in "
                + "the workflow supplies; it would resolve to the no-data sentinel at run time.")));
        failures.AddRange(AgentTemplateGrammar.LoopProducerWarnings(workflow)
            .Select(static warning => new WorkflowCheckFailure(WorkflowCheck.AgentTags, warning)));

        failures.AddRange(_gates.Check(workflow, _gates.RunLimits));

        failures.AddRange((await SignatureFieldSaveSynchronizer.CheckDeletionsAsync(
                _coreDbContext, workflowTypeId, previous, workflow, cancellationToken))
            .Select(static error => new WorkflowCheckFailure(WorkflowCheck.SignatureFields, error.Description)));

        return failures;
    }

    private static string Describe(Error error)
    {
        return string.IsNullOrWhiteSpace(error.Code) ? error.Description : $"{error.Code}: {error.Description}";
    }
}
