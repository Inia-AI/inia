using ErrorOr;
using IniaAi.Application.Workflows.EditorAgent;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>
/// The version note an editor save carries when it holds changes the agent proposed: read before
/// the save, and recorded as saved only once the save wrote a new version, so a failed or
/// unchanged save loses nothing.
/// </summary>
public sealed class EditorAgentVersionNote
{
    private readonly IWorkflowEditorAgentService _service;
    private readonly string _organizationId;
    private readonly string _userId;
    private EditorAgentSaveNote? _pending;
    private bool _noteRead;
    private bool _acceptedSinceSave;

    public EditorAgentVersionNote(IWorkflowEditorAgentService service, string organizationId, string userId)
    {
        _service = service;
        _organizationId = organizationId;
        _userId = userId;
    }

    /// <summary>
    /// Changes of the agent landed in this editor. Until then a save skips reading the note: the
    /// workflow it saves holds no change accepted here.
    /// </summary>
    public void NoteAccepted()
    {
        _acceptedSinceSave = true;
    }

    /// <summary>The note for the save about to be sent, or null when it carries no agent changes.</summary>
    public async Task<string?> BeforeSaveAsync(string workflowTypeId)
    {
        _pending = null;
        _noteRead = false;
        if (!_acceptedSinceSave || !_service.IsEnabled || string.IsNullOrWhiteSpace(workflowTypeId))
        {
            return null;
        }

        ErrorOr<EditorAgentSaveNote?> note = await _service.PeekSaveNoteAsync(_organizationId, _userId, workflowTypeId);
        _noteRead = !note.IsError;
        _pending = note.IsError ? null : note.Value;
        return _pending?.Text;
    }

    /// <summary>
    /// The save wrote <paramref name="versionId"/>, a new version carrying the note: its changes
    /// count as saved, and the report feedback they fix as addressed in that version. True when
    /// the note was recorded, so the conversation can show those changes as saved.
    /// </summary>
    public async Task<bool> AfterSaveAsync(string workflowTypeId, bool wroteNewVersion, string versionId)
    {
        EditorAgentSaveNote? pending = _pending;
        _pending = null;
        if (!wroteNewVersion || !_noteRead)
        {
            return false;
        }

        _acceptedSinceSave = false;
        if (pending is null)
        {
            return false;
        }

        return !(await _service.CommitSaveNoteAsync(_organizationId, _userId, workflowTypeId, pending, versionId)).IsError;
    }
}
