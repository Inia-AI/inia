---
type: explanation
status: current
description: "Explains the per-user workflow-type visibility filter: a display shortlist that narrows pickers and report lists without changing access. Read when a user cannot find a workflow type in a picker or a run in the Reports list, or when you set a user's shortlist."
---

# Workflow Type Visibility

**This is a display filter, not authorization.** A user's workflow-type visibility selection changes what a few list endpoints *show* them; it grants nothing, gates nothing, and is not part of the [authorization model](authorization.md). Which workflow types an organization may use is [`WorkflowTypeOrganizationAccess`](authorization.md#workflow-type-access).

Some workflow types are vital to the users who work with them and noise to everyone else. The visibility filter lets an administrator give each user a shortlist of the workflow types they actually use, so their pickers, Reports table, and Dashboard stop showing everything the organization can run.

## Semantics

- **No selection (the default for every user)**: the user sees every workflow type their organization has access to. Nothing changes until an administrator sets a shortlist.
- **A selection only narrows.** The chosen set is applied *on top of* the organization's access — in the type pickers it is intersected with `WorkflowTypeOrganizationAccess`, so choosing a type the organization cannot reach shows nothing. A selection can never widen what the organization allows.
- **It is an allowlist, not a blocklist.** There is no "hide this one type" — a selection means *only these types*. Hiding one type from a user means selecting every other type they should still see.
- **One selection per user, across all organizations.** The selection is keyed by user alone (no organization column), so a user who belongs to several organizations carries the same shortlist into each of them.

## What it narrows — and what it does not

Reads are narrowed by `UserWorkflowTypeVisibilityResolver`, which resolves the caller's chosen type ids and hands them to the two read paths:

| Surface | Narrowed? |
|---|---|
| `GET /workflow-categories/summaries` — the type pickers (`ListWorkflowCategorySummariesQueryHandler`) | Yes |
| `GET /workflow-reports` — the Reports table and the Dashboard (`WorkflowExecutionsForListingSpec`) | Yes |
| `GET /workflow-categories` (the heavier, non-summaries listing) | No |
| `GET /workflow-reports/{id}` — reading one report by id | No |
| `POST /workflow-executions/submit` — running a workflow | No |

The last three rows are the point: a hidden type is still fully **reachable and runnable** — by direct id, by `/submit`, or through the un-narrowed listing. That is by design; anything else would make a display preference an access control. It also means the filtered Reports list is not a reliable inventory of the organization's executions — an audit-style enumeration should use an account with no selection.

## Managing a user's selection

In the Blazor admin UI, each row of **User Management** has a filter button that opens the *Workflow Types* dialog (`UserWorkflowTypeVisibilityDialog`):

- A **"Show only selected workflow types"** switch controls whether a shortlist exists. Turning it **off** and saving deletes the user's selection — back to seeing everything.
- With the switch on, the dialog lists the selected types and offers the rest for adding. The picker is the union of workflow types accessible to the user's **active** organization memberships, excluding the internal `Generic` and `TestWorkflowType` types.
- Saving with the switch on and nothing selected is refused (`Select at least one workflow type, or turn off "Show only selected workflow types".`) — an empty shortlist would read as "sees nothing", which this feature deliberately cannot express.
- Types whose organization access has been revoked since the selection was made drop out of the picker and their rows are deleted on the next save.

The dialog writes repository-direct from the Blazor server (the same pattern as the workflow-type tenant assignment dialog); there is no v1 API endpoint for managing selections.

## Storage

Selections live in the UserData table `UserWorkflowTypeVisibilities` — schema in the [UserData database reference](../../reference/database/user-data-database.md#userworkflowtypevisibilities).
