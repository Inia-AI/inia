---
type: explanation
status: current
description: "Explains report feedback: where a reader leaves it on a finished report, who may list, delete, triage and read it across organizations, what an item records and its limits, its Open, Addressed and Dismissed lifecycle, and its storage. Read when you work on report feedback, triage it, or the editor agent's Use feedback."
---

# Report Feedback

A reader of a finished report can say, in place, what is wrong with it. The item is stored beside the execution, so the people who tune the template can later read every organization's feedback on a workflow type in one place. This page describes leaving, listing and triaging feedback. The [editor agent](editor-agent.md#what-it-does) works on it through **Use feedback**, and marks an item addressed when a save carries its fix.

**Feedback belongs to the report, not to an output element.** An item carries no link to a report paragraph, a block or a `StableBlockId`, in any form: blocks are edited, moved and deleted between template versions, and data keyed on them has broken before. The only pointer into the report's content is the reader's own text selection, kept as plain quoted text. The execution already records the template version it ran against, so the item does not repeat it; read `WorkflowExecution.TemplateVersionId` through the execution.

## Where it happens

The analyst workspace's report view (`/workflow-reports/{id}/edit` in the SPA) opens feedback two ways, both into the same panel under the header:

- Highlighting text in the report body shows a small popup with **Feedback** beside **Explore**. Choosing it opens the panel with the highlighted text as the item's quoted text.
- The report header's actions menu has a **Feedback** entry, beside export, re-run and the other report-level actions. If the reader has text selected in the report body at that moment, the selection is quoted; otherwise the item is about the report as a whole.

Either way, keyboard focus moves to the panel's heading, **Feedback on this report**, which also names the panel as a region, so a screen reader announces that it opened. **Close** returns focus to the header's **Actions** button.

The form takes:

| Field | Required | Notes |
|---|---|---|
| Category | yes | One of `WrongFigure`, `MissingInformation`, `WrongClassification`, `WordingOrStyle`, `NotRelevant` |
| Comment | yes | Free text |

Existing items on the report are listed in the panel with the category, the quoted text when there is one, the author's display name and the status. Each item has a **Delete** button; it asks in an in-app dialog ("Delete this feedback?") before it deletes the item for good. There is no edit of an item.

## Who may do what

- **Leave and list feedback**: any authenticated user of the organization that owns the execution. The list is org-scoped: a client sees only its own organization's items, including the status an editor gave each one.
- **Delete an item**: anyone who can list it, whatever its status, through `DELETE /workflow-reports/{reportId}/feedback/{feedbackId}` (`DeleteReportFeedbackCommandHandler`). It carries no role gate and the same organization scope as the list (`ReportFeedbackByIdTrackedSpec`), so an organization never deletes another's item: that item answers 404 and stays. The editor agent's **Choose feedback** dialog deletes through `IWorkflowEditorAgentService.DeleteFeedbackAsync`, in the scope that dialog lists from: the caller's organization, or every organization for a SuperAdmin ([editor agent](editor-agent.md#what-it-does)).
- **Set the status**: `Roles.EDITOR_ROLES` (`WorkflowEditor` and `SuperAdmin`) only. The status control appears in the SPA for those roles and the endpoint carries the same gate.
- **Read every organization's feedback on a workflow type**: `SuperAdmin` only (`Roles.SUPER_ADMIN`), through `GET /workflow-types/{workflowTypeId}/feedback`. Not `WorkflowEditor`: an organization Admin can grant that role inside its own organization, so it cannot guard a read that crosses organizations. The controller carries the gate, and `ListWorkflowTypeFeedbackQueryHandler` answers 403 to any caller the controller did not mark as a SuperAdmin. No page in the SPA lists it. This is the one read of feedback that crosses the tenant boundary, by design; the [multi-tenant page](../architecture/multi-tenant/index.md#shared-vs-tenant-isolated-data) names it as such, and each item on it carries its `organizationId`.

The role gates are listed on the [authorization matrix](user-management/authorization.md#the-canonical-role-gate-matrix-v1-api).

## What an item records

| Field | Source |
|---|---|
| `workflowExecutionId` | the report the reader was on |
| `workflowTypeId` | copied from the execution at creation (`WorkflowExecution.WorkflowTypeId`) |
| `quotedText` | the reader's text selection, when any; at most 4,000 characters |
| `category`, `comment` | what the reader said; the comment holds at most 4,000 characters |
| `authorUserId`, `authorDisplayName` | who recorded it; the name is resolved on read, the id is stored |
| `status`, `dismissReason`, `statusChangedByUserId` | the editor's triage, see below; a dismiss reason holds at most 500 characters |
| `addressedInVersionId` | the workflow version whose save carried the editor agent's fix; null otherwise |
| `createdAt`, `updatedAt` | UTC |

`CreateReportFeedbackCommandHandler` resolves the execution within the caller's organization and refuses with not-found when it is not there; nothing else is validated against the report's content.

## Lifecycle

An item is created `Open`. An editor moves it to `Addressed` or `Dismissed`; a dismissal must carry a short `dismissReason`, moving the item to any other status clears the reason, and the editor's id is recorded as `statusChangedByUserId` (`ReportFeedbackItem.SetStatus`). Clients see the status on their own items but cannot change it.

The [editor agent](editor-agent.md#saving) changes the status in two ways, both through an editor's own action. A save of a change the agent named for an item marks the item `Addressed`, records the saving user as `statusChangedByUserId` and the saved version as `addressedInVersionId` (`ReportFeedbackItem.MarkAddressed`). **Dismiss**, on an item the agent suggested dismissing, dismisses it with the agent's reason. Any later status change clears `addressedInVersionId`.

Deleting an item removes the row for good, in any status; nothing else changes. An editor agent message that was sent the item keeps the copy staged for its run and still shows how many items it was sent. A save that names a deleted item skips it, and the agent's dismiss suggestion for it no longer shows.

## Storage

Items live in the UserData table `ReportFeedbackItems`, indexed on `(OrganizationId, WorkflowExecutionId)` for the org-scoped read of one report and on `(WorkflowTypeId, Status)` for the SuperAdmin cross-organization read. Schema in the [UserData database reference](../reference/database/user-data-database.md#reportfeedbackitems). An item has no foreign key to its execution; when the soft-delete sweep purges the execution, it deletes the execution's feedback too, because the quoted text is the report's own content (`SoftDeleteCleanupService.LooseReferenceDispositions`).

## API

The five endpoints are documented on the [Workflow Reports API](../reference/api/reports.md#report-feedback) page.

## Related

- [Workflow Template Versioning](workflow-template-versioning.md), where the execution's `TemplateVersionId` comes from.
- [Reading a report](../guides/workflows/reading-a-report.md).
