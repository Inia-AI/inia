using System.Text.RegularExpressions;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>
/// The agent's steps in the editor's words. The files it reads are the workflow, not documents,
/// so a read or search of them is named by what the file holds. Every step is plain text, and
/// no block id shows.
/// </summary>
public static partial class EditorAgentSteps
{
    public const string READ_WORKFLOW = "Read the workflow";
    public const string READ_OUTLINE = "Read the outline of the workflow";
    public const string READ_CONNECTIONS = "Looked at how blocks connect";
    public const string READ_VERSIONS = "Read the saved versions";
    public const string SEARCHED_WORKFLOW = "Searched the workflow";
    public const string CHECKED_FILES = "Checked the workflow files";

    public static string Label(AgentActivityBeat beat)
    {
        return beat.Label switch
        {
            AgentActivityMapping.READ_CONVERTED_TEXT_LABEL => ReadLabel(beat.DocumentNames),
            AgentActivityMapping.SEARCHED_DOCUMENTS_LABEL => SearchLabel(beat.DocumentNames),
            AgentActivityMapping.CHECKED_DOCUMENT_SET_LABEL => CHECKED_FILES,
            _ => Plain(beat.Label),
        };
    }

    public static string? Detail(AgentActivityBeat beat)
    {
        return string.IsNullOrWhiteSpace(beat.Detail) ? null : Plain(beat.Detail);
    }

    private static string ReadLabel(IReadOnlyList<string> documents)
    {
        if (documents.Contains(EditorAgentStaging.DEPENDENCY_MAP_DOCUMENT))
        {
            return READ_CONNECTIONS;
        }

        if (documents.Contains(EditorAgentStaging.OUTLINE_DOCUMENT))
        {
            return READ_OUTLINE;
        }

        if (documents.Contains(EditorAgentStaging.VERSIONS_DOCUMENT))
        {
            return READ_VERSIONS;
        }

        return READ_WORKFLOW;
    }

    private static string SearchLabel(IReadOnlyList<string> documents)
    {
        return documents.Contains(EditorAgentStaging.DEPENDENCY_MAP_DOCUMENT) ? READ_CONNECTIONS : SEARCHED_WORKFLOW;
    }

    private static string Plain(string text)
    {
        return BlockIdRegex().Replace(AgentActivityPlainText.From(text), string.Empty).Trim();
    }

    [GeneratedRegex(@"\s*\(?block:[A-Za-z0-9_.:-]{6,}\)?")]
    private static partial Regex BlockIdRegex();
}
