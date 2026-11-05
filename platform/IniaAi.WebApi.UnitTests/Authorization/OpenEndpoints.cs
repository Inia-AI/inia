namespace IniaAi.WebApi.UnitTests.Authorization;

/// <param name="Action">The endpoint as Controller.Action, the controller's name without its suffix.</param>
/// <param name="Anonymous">Whether it is open without signing in, not only to every signed-in user.</param>
/// <param name="Reason">Why every caller it admits may call it.</param>
public sealed record OpenEndpoint(string Action, bool Anonymous, string Reason);

/// <summary>
/// The endpoints with no role gate, each with the reason it is open. <see cref="EndpointGateTests"/>
/// fails on an endpoint with no gate that is not here, so opening one is a decision a reviewer sees.
/// A role gate keeps staff work from customers; the capability gate, named in a reason where it
/// applies, is what keeps a self-serve organization out.
/// </summary>
public static class OpenEndpoints
{
    private const string SECOND_FACTOR = "Manages the caller's own second factor.";
    private const string TRUSTED_DEVICES = "Manages the caller's own trusted devices.";
    private const string OWN_PRESET = "The caller's own refinement presets; only the owner changes or deletes one. The refine capability governs it.";
    private const string COUNTERPARTIES = "Analysts keep their organization's counterparties: a run is about one.";
    private const string DOCUMENTS = "Analysts read the documents their organization uploaded.";
    private const string STANDALONE_PROCESSING = "Pages a document outside a run; the standalone-processing and upload capabilities govern it.";
    private const string RUN_LIST = "The run screen lists the workflows the organization may run.";
    private const string RUN = "Starts or checks a run of a workflow the organization may run; the run capability governs it.";
    private const string RUN_LOGS = "The log of the organization's run; the run-logs capability keeps it from self-serve, because it shows workflow instructions.";
    private const string RUN_EVIDENCE = "The handler admits staff roles and the service user that started the run; the run-evidence capability keeps it from self-serve.";
    private const string REPORT_READ = "Analysts read and export their organization's reports.";
    private const string REPORT_EDIT = "Analysts edit their organization's reports before they confirm them.";
    private const string FEEDBACK = "Any reader of a report leaves, reads and deletes feedback on it, within the organization.";
    private const string EXPLORE = "Explains where a report's figure came from; the explore capability governs it.";
    private const string META = "Deployment metadata for monitoring and support; it names no customer data.";

    public static readonly IReadOnlyList<OpenEndpoint> All =
    [
        new("AiContentRefinement.Refine", false, "Refines text the caller supplies; the refine capability governs it."),
        new("AiContentRefinement.GetPresets", false, OWN_PRESET),
        new("AiContentRefinement.CreatePreset", false, OWN_PRESET),
        new("AiContentRefinement.UpdatePreset", false, OWN_PRESET),
        new("AiContentRefinement.DeletePreset", false, OWN_PRESET),
        new("Authentication.Exchange", true, "The token endpoint: signing in is how a caller gets a token."),
        new("Authentication.VerifyMfa", true, "Completes a sign-in with its second factor, against the sign-in's MFA token."),
        new("Authentication.SendEmailOtp", true, "Sends the second-factor code of a sign-in in progress, against its MFA token."),
        new("Counterparties.GetCounterparties", false, COUNTERPARTIES),
        new("Counterparties.GetById", false, COUNTERPARTIES),
        new("Counterparties.GetPeerCandidates", false, COUNTERPARTIES),
        new("Counterparties.Create", false, COUNTERPARTIES),
        new("Counterparties.Update", false, COUNTERPARTIES),
        new("Counterparties.Delete", false, COUNTERPARTIES),
        new("CounterpartyTypes.GetCounterparties", false, "The run form lists the counterparty types a workflow accepts."),
        new("DocumentCategories.GetDocumentCategories", false, "The upload form lists the document categories."),
        new("Documents.ListDocuments", false, DOCUMENTS),
        new("Documents.GetDocument", false, DOCUMENTS),
        new("Documents.ListDocumentPages", false, DOCUMENTS),
        new("Documents.GetDocumentPage", false, DOCUMENTS),
        new("Documents.DownloadDocumentText", false, DOCUMENTS),
        new("Documents.UpdateDocument", false, "Analysts correct the details of their organization's documents, such as the class."),
        new("Documents.ProcessDocument", false, STANDALONE_PROCESSING),
        new("Documents.CreateTextDocument", false, STANDALONE_PROCESSING),
        new("ExecutedWorkflows.Download", false, RUN_EVIDENCE),
        new("ExternalAuth.GetProviders", true, "The sign-in screen reads which sign-in methods to offer before anyone has signed in."),
        new("Files.GetFileAsync", false, "Downloads a file of the caller's organization, such as a document a report cites."),
        new("Files.UploadAsync", false, "Uploads a document for a run; the upload capability governs it."),
        new("Jobs.GetWorkflowOutputs", false, "Lists the caller's runs in progress."),
        new("Jobs.GetJobById", false, "Reads one of the caller's runs in progress; another user's job is refused."),
        new("Meta.GetBuildInfo", true, META),
        new("Meta.GetUptime", true, META),
        new("Meta.GetVersion", true, META),
        new("Pages.GetPageImageAsync", false, "Shows a page of an organization's document beside the report that cites it."),
        new("Passwords.ForgotPasswordAsync", true, "Sends a reset link, and answers the same whether or not the address has an account."),
        new("Passwords.ResetPasswordAsync", true, "Sets a new password; the token from the reset mail is the caller's proof."),
        new("Profile.ChangePasswordAsync", false, "Changes the caller's own password."),
        new("TokenUsage.GetMyUsageSummary", false, "The caller's own token usage; the usage-cost capability keeps it from self-serve."),
        new("TwoFactor.GetStatus", false, SECOND_FACTOR),
        new("TwoFactor.SetupTotp", false, SECOND_FACTOR),
        new("TwoFactor.EnableTotp", false, SECOND_FACTOR),
        new("TwoFactor.DisableTotp", false, SECOND_FACTOR),
        new("TwoFactor.RegenerateRecoveryCodes", false, SECOND_FACTOR),
        new("TwoFactor.GetTrustedDevices", false, TRUSTED_DEVICES),
        new("TwoFactor.RemoveTrustedDevice", false, TRUSTED_DEVICES),
        new("TwoFactor.RemoveAllTrustedDevices", false, TRUSTED_DEVICES),
        new("Users.GetMyTenants", false, "Lists the organizations the caller belongs to, which the app needs before it picks one."),
        new("Users.GetUsers", false, "Lists the people of the caller's organization; the team-view capability keeps it from self-serve."),
        new("Users.ResolveUsers", false, "Names the people who created the organization's reports, and nobody else."),
        new("WorkflowCategories.GetWorkflowCategories", false, RUN_LIST),
        new("WorkflowCategories.GetWorkflowCategorySummaries", false, RUN_LIST),
        new("WorkflowCategories.ValidateDocumentClasses", false, "Checks the documents of a run before it is submitted."),
        new("WorkflowExecutions.StartWorkflowExecution", false, RUN),
        new("WorkflowExecutions.Submit", false, RUN),
        new("WorkflowExecutions.GetReRunInputs", false, RUN),
        new("WorkflowExecutions.UpdateState", false, "Cancels a run of the caller's organization."),
        new("WorkflowExecutions.GetExecutionLogs", false, RUN_LOGS),
        new("WorkflowExecutions.GetExecutionLogById", false, RUN_LOGS),
        new("WorkflowExecutions.GetExecutionLogPlaceholders", false, RUN_LOGS),
        new("WorkflowExecutions.GetAgentActivity", false, RUN_LOGS),
        new("WorkflowExecutions.ListAgentRunFiles", false, RUN_EVIDENCE),
        new("WorkflowExecutions.DownloadAgentRunFile", false, RUN_EVIDENCE),
        new("WorkflowExecutions.DownloadAgentRunTrace", false, RUN_EVIDENCE),
        new("WorkflowExecutionsGraph.ValidateTarget", false, EXPLORE),
        new("WorkflowExecutionsGraph.QueryProvenance", false, EXPLORE),
        new("WorkflowReports.Get", false, REPORT_READ),
        new("WorkflowReports.GetById", false, REPORT_READ),
        new("WorkflowReports.GetReportSections", false, REPORT_READ),
        new("WorkflowReports.GetReportDataOutputs", false, REPORT_READ),
        new("WorkflowReports.Export", false, REPORT_READ),
        new("WorkflowReports.GetCost", false, "The handler limits it to the report's organization; the run-evidence capability keeps it from self-serve."),
        new("WorkflowReports.UpdateReportParagraph", false, REPORT_EDIT),
        new("WorkflowReports.UpdateReportSection", false, REPORT_EDIT),
        new("WorkflowReports.DeleteReportParagraph", false, REPORT_EDIT),
        new("WorkflowReports.DeleteReportSection", false, REPORT_EDIT),
        new("WorkflowReports.DeleteById", false, "Analysts delete their organization's reports."),
        new("WorkflowReports.ListFeedback", false, FEEDBACK),
        new("WorkflowReports.CreateFeedback", false, FEEDBACK),
        new("WorkflowReports.DeleteFeedback", false, FEEDBACK),
        new("WorkflowTemplates.GetSummary", false, "The run form reads a workflow's variables and document classes."),
    ];
}
