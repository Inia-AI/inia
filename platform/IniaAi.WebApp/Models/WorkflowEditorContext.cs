using IniaAi.Application.Common.Signatures;
using IniaAi.Common.Constants;
using IniaAi.Common.Extensions;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Documents;
using IniaAi.Services.Jobs;
using IniaAi.WebApp.Services;
using MudBlazor;
using System.Diagnostics.CodeAnalysis;

namespace IniaAi.WebApp.Models;

public class WorkflowEditorContext
{
    private const string NO_ORGANIZATION_MESSAGE =
        "Select an organization first: the documents an uploaded email yields are stored under it.";

    private readonly Dictionary<string, List<DocumentInfo>> _productsByExpandedEmailId = [];

    private readonly HashSet<string> _removedDocumentIds = [];

    private (string Mode, string? AttachmentsDocumentClass)? _appliedEmailIntake;

    public IWorkflowHandler WorkflowHandler { get; set; }

    private Workflow _workflow;

    /// <summary>The workflow being edited; setting it records whether it carries a configuration.</summary>
    public Workflow Workflow
    {
        get => _workflow;
        [MemberNotNull(nameof(_workflow))]
        set
        {
            _workflow = value;
            MarkSaved();
        }
    }

    /// <summary>
    /// Whether the workflow carried no configuration when it was loaded or last saved. A setting the
    /// author turns on and off again then leaves no configuration, so the save mints no version;
    /// it outlives the tabs, which the editor builds again on every switch.
    /// </summary>
    public bool ConfigurationAbsentWhenSaved { get; private set; }

    public string WorkflowTypeId { get; set; }

    public Counterparty? Counterparty { get; set; }

    public List<Counterparty>? PeerCompanies { get; set; }

    public List<DocumentInfo> Documents { get; set; } = [];

    public EmailIntakeSelection EmailIntake { get; set; } = new();

    public EditorDocumentServices? DocumentServices { get; init; }

    /// <summary>Called when a test run's document load adds or removes documents in <see cref="Documents"/>.</summary>
    public Func<Task>? OnDocumentsChanged { get; init; }

    public Dictionary<string, string> VariableValues { get; set; } = [];

    /// <summary>
    /// Signature field renames performed in this editor session, in order, awaiting the next
    /// Save-As-Core — the save transaction rewrites client override rows to the new names.
    /// Cleared after a successful save.
    /// </summary>
    public List<SignatureFieldRename> PendingSignatureFieldRenames { get; } = [];

    /// <summary>
    /// Raised by the notice above the editor that says the workflow has no variables. The editor's
    /// tab strip, further down the same page, opens its Variables tab.
    /// </summary>
    public event Action? VariablesTabRequested;

    public WorkflowEditorContext(
        string workflowTypeId,
        Workflow workflow,
        IWorkflowHandler workflowHandler)
    {
        Workflow = workflow;
        WorkflowTypeId = workflowTypeId;
        WorkflowHandler = workflowHandler;
    }

    /// <summary>Records the workflow's configuration as saved, after a save or a load.</summary>
    public void MarkSaved()
    {
        ConfigurationAbsentWhenSaved = _workflow.Configuration is null;
    }

    public void RequestVariablesTab()
    {
        VariablesTabRequested?.Invoke();
    }

    /// <summary>
    /// The document rows one report run starts with: a fresh row per editor document, carrying the
    /// class and processing choices made in the editor. A run writes its own decisions onto its
    /// rows (the class a stage settles on, a document a stage takes out of the run), so a row
    /// shared with the editor would carry them into the next report started from it. The documents a
    /// test run expanded from an email stay out: the report expands the email itself, with the intake
    /// choice made for the report and through the report's own intake gate.
    /// </summary>
    public IReadOnlyList<DocumentInfo> RunDocumentsFor(string jobId)
    {
        HashSet<DocumentInfo> testRunProducts = [.. _productsByExpandedEmailId.Values.SelectMany(products => products)];

        return [.. Documents.Where(editorDocument => !testRunProducts.Contains(editorDocument)).Select(editorDocument => new DocumentInfo(editorDocument.DocumentId)
        {
            Document = editorDocument.Document,
            DocumentClass = editorDocument.DocumentClass,
            ShouldUseOcr = editorDocument.ShouldUseOcr,
            ShouldBeDisplayedInOutput = editorDocument.ShouldBeDisplayedInOutput,
            JobId = jobId,
        })];
    }

    public async Task LoadInputContextToWorkflowAsync()
    {
        EditorDocumentServices services = DocumentServices
            ?? throw new InvalidOperationException($"{nameof(DocumentServices)} must be set before a test run loads documents.");
        IUserDataUnitOfWork userDataUnitOfWork = services.UserDataUnitOfWork;

        (bool documentsChanged, IReadOnlyList<SkippedProtectedFile> skippedAttachments) = await ExpandEmailsForTestRunAsync(
            services.EmailIntakeStage,
            () => new EmailIntakeTarget(
                services.AppState.CurrentOrganizationId ?? throw new InvalidOperationException(NO_ORGANIZATION_MESSAGE),
                services.AppState.User?.Id,
                services.AppState.IsTrustedEditor,
                JobId: null),
            userDataUnitOfWork);

        foreach (SkippedProtectedFile skipped in skippedAttachments)
        {
            services.AppState.Notify($"The attachment {skipped.FileName} was not read: {skipped.Reason}", Severity.Warning);
        }

        if (documentsChanged)
        {
            _ = await userDataUnitOfWork.CompleteAsync();

            if (OnDocumentsChanged is not null)
            {
                await OnDocumentsChanged();
            }
        }

        PruneRemovedDocuments(WorkflowHandler.AllPages);

        await services.DocumentProcessing.ProcessDocumentsAsync(
            [.. Documents],
            userDataUnitOfWork,
            Workflow,
            workflowTypeId: WorkflowTypeId,
            userId: Workflow.UserId);

        await services.DocumentProcessing.LoadContextToWorkflowAsync(
            Documents,
            Workflow,
            WorkflowHandler.AllPages,
            onDocumentProcessed: async documentId =>
            {
                services.Logger.LogInformation("Saving document {DocumentId} after loading to workflow", documentId);

                _ = await userDataUnitOfWork.CompleteAsync();
            });

        _ = await userDataUnitOfWork.CompleteAsync();
    }

    /// <summary>
    /// Adds the documents an uploaded email yields — its body as a PDF and, when chosen, its
    /// attachments with their class — to the editor's documents, as the report job's email intake
    /// does. An email is read once per intake choice: later runs reuse its products, and when the
    /// choice changes the old products leave before the emails are expanded again. Reports whether
    /// <see cref="Documents"/> changed.
    /// </summary>
    public async Task<(bool Changed, IReadOnlyList<SkippedProtectedFile> Skipped)> ExpandEmailsForTestRunAsync(
        EmailIntakeJobStage emailIntake,
        Func<EmailIntakeTarget> target,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken = default)
    {
        (string Mode, string? AttachmentsDocumentClass) choice = EmailIntakeModes.Resolve(
            EmailIntake.ExtractAttachments,
            EmailIntake.AttachmentsDocumentClass);
        bool changed = false;

        if (_appliedEmailIntake is not null && _appliedEmailIntake != choice)
        {
            foreach (DocumentInfo product in _productsByExpandedEmailId.Values.SelectMany(products => products))
            {
                if (Documents.Remove(product))
                {
                    userDataUnitOfWork.Documents.RemoveDocumentInfo(product);
                    _ = _removedDocumentIds.Add(product.DocumentId);
                    changed = true;
                }
            }

            _productsByExpandedEmailId.Clear();
        }

        _appliedEmailIntake = choice;

        List<DocumentInfo> unexpandedEmails = [.. Documents.Where(row =>
            row.Document?.MediaType.IsEmail() == true && !_productsByExpandedEmailId.ContainsKey(row.Id))];

        if (unexpandedEmails.Count == 0)
        {
            return (changed, []);
        }

        var outcome = new EmailIntakeOutcome();

        try
        {
            await emailIntake.ExpandEmailsAsync(
                target(),
                Documents,
                unexpandedEmails,
                choice.Mode,
                choice.AttachmentsDocumentClass ?? string.Empty,
                workflowExecutionId: null,
                userDataUnitOfWork,
                outcome,
                cancellationToken);
        }
        finally
        {
            changed |= TrackExpansion(outcome, userDataUnitOfWork);
        }

        return (changed, outcome.SkippedFiles);
    }

    /// <summary>
    /// Keeps what the emails that finished yielded, and discards the rows minted for an email that
    /// failed part-way, so a failure neither commits orphan rows nor expands a finished email again.
    /// </summary>
    private bool TrackExpansion(EmailIntakeOutcome outcome, IUserDataUnitOfWork userDataUnitOfWork)
    {
        bool changed = false;

        foreach (string emailId in outcome.HandledEmailIds)
        {
            _ = _productsByExpandedEmailId.TryAdd(emailId, []);
        }

        foreach (EmailIntakeProduct product in outcome.Products)
        {
            if (_productsByExpandedEmailId.TryGetValue(product.SourceEmailId, out List<DocumentInfo>? products))
            {
                Documents.Add(product.Document);
                products.Add(product.Document);
                changed = true;
            }
            else
            {
                userDataUnitOfWork.Documents.RemoveDocumentInfo(product.Document);
            }
        }

        return changed;
    }

    /// <summary>
    /// Forgets a document the user removed from the upload card. When it is an expanded email, the
    /// documents it yielded leave <see cref="Documents"/> too, and are returned so the caller can
    /// delete their rows; a removed product is simply no longer tracked. The next test run takes every
    /// removed document off the blocks and pages it was loaded onto.
    /// </summary>
    public IReadOnlyList<DocumentInfo> ForgetDocument(DocumentInfo removed)
    {
        _ = _removedDocumentIds.Add(removed.DocumentId);

        foreach (List<DocumentInfo> products in _productsByExpandedEmailId.Values)
        {
            _ = products.Remove(removed);
        }

        if (!_productsByExpandedEmailId.Remove(removed.Id, out List<DocumentInfo>? derived))
        {
            return [];
        }

        List<DocumentInfo> leaving = [];
        foreach (DocumentInfo product in derived)
        {
            if (Documents.Remove(product))
            {
                leaving.Add(product);
                _ = _removedDocumentIds.Add(product.DocumentId);
            }
        }

        return leaving;
    }

    /// <summary>
    /// Takes the documents the editor removed off every block and off <paramref name="allPages"/>.
    /// Loading only adds, so without this a removed or replaced document would keep feeding later test
    /// runs. A document that is listed again, for example uploaded anew, stays.
    /// </summary>
    public void PruneRemovedDocuments(List<IniaAi.Domain.Entities.Documents.Page> allPages)
    {
        _removedDocumentIds.ExceptWith(Documents.Select(row => row.DocumentId));
        if (_removedDocumentIds.Count == 0)
        {
            return;
        }

        _ = allPages.RemoveAll(page => _removedDocumentIds.Contains(page.DocumentId));
        foreach (IniaAi.Domain.Entities.Workflows.Blocks.Block block in Workflow.GetAllBlocks())
        {
            _ = block.Documents.RemoveAll(document => _removedDocumentIds.Contains(document.Id));
        }

        _removedDocumentIds.Clear();
    }

    public static async Task<Workflow> LoadCoreWorkflowAsync(
        AppState appState,
        ILogger logger,
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowType workflowType,
        User user,
        Action<List<string>> updateDocumentClassesCallback)
    {
        Workflow? coreWorkflow = null;

        try
        {
            coreWorkflow = await coreUnitOfWork
                .Workflows
                .GetByTypeIdAsNoTrackingAsync(workflowType.Id);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load core workflow");
            appState.Notify(
                "Failed to load core workflow, creating an empty workflow",
                Severity.Error);
        }

        if (coreWorkflow is null)
        {
            logger.LogError(
                "Core workflow '{Workflow}' was not found. Creating an empty workflow.",
                workflowType.Id);

            return new(workflowType.Name)
            {
                UserId = user.Id,
                OrganizationId = user.OrganizationId,
                WorkflowTypeId = workflowType.Id,
                WorkflowType = workflowType,
            };
        }

        Workflow userWorkflow = new();

        coreWorkflow.CloneToPreservingIds(userWorkflow);
        userWorkflow.UserId = user.Id;
        userWorkflow.OrganizationId = user.OrganizationId;

        NormalizeParameterizedSections(userWorkflow);

        List<string> documentClasses = userWorkflow.GetDocumentClasses();
        updateDocumentClassesCallback.Invoke(documentClasses);

        return userWorkflow;
    }

    public static void NormalizeParameterizedSections(Workflow workflow)
    {
        foreach (Section section in workflow.Sections)
        {
            _ = ParameterizedSectionNormalizer.Normalize(section);
        }
    }
}
