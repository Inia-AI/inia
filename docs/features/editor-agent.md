---
type: explanation
status: current
description: "Agent mode in the workflow editor: how to open it, what the agent does (answers, reads past reports and report feedback, proposes changes, model choice, workflow notes), how a proposal is reviewed and saved, what it never does, how a deployment switches it on, and its cost and daily spend limit. Read when you use or support Agent mode in the workflow editor, or a proposal cannot be accepted."
---

# Editor Agent

**On this page:** Opening it · What it does · What it cannot do · Switching it on · Cost · Related pages

**Agent mode** puts a conversation with an agent over the workflow editor. The agent reads the
workflow open in the editor and answers questions about it, and when a change would help it
proposes one for the user to review. It runs on the [agent runtime](../architecture/services/infrastructure/agent-runtime-integration.md#agent-conversations);
how its turns run, wait, settle and are deleted is described there.

## Opening it

**Work with the agent**, in the app bar of the workflow editor, opens Agent mode; **Ctrl+J** (or **⌘J**
on a Mac) opens and closes it from anywhere in the editor. A one-time hint beside the button
("Ask questions about this workflow or request changes") goes once dismissed with **Got it** or
once Agent mode has been used. **Back to editor** in the app bar, the same button at the top left
of the panel, or **Close Agent mode** returns to the editor. The browser remembers whether Agent
mode was on, so the editor reopens in it.

Agent mode covers the editor with the conversation. The editor stays loaded underneath, so changes
the user accepts land on it, and **Open block** on a change, or **Open in editor** in a block's
detail drawer, returns to the editor at that block.

Only users who hold `WorkflowEditor` or `SuperAdmin` see it, and only on a deployment that switched
it on (below). Some editor pages also admit monitoring roles; those users do not see it.

## What it does

The agent knows what each block type and setting does, not only its name. The workflow schema it
works from carries a description for every block type, setting and allowed value, taken from the
platform's code comments, and a test fails when a block type or setting has none. Its lookup helper
answers `wf.py explain <type> [setting]` from those descriptions. A short features page covers what
single blocks do not show: checks that are not guarantees (an output format checks the answer and
retries once, it does not force the shape), the `AF11` stop term, variants, loops, Library links and
the blocks that call outside services.

**Workflow notes.** Each workflow has a **Workflow notes** field, under the description at the top of
the editor: what the workflow is for and how it should work (the report's purpose and readers, what
each section should produce, conventions, client rules and decisions, known weak spots). The notes
are saved with the workflow in the same version. The agent reads them first with every message and
follows them. When its change affects something they describe, it updates them in the same
proposal, as a **Workflow notes** change you accept or reject like any other. When they are empty,
it writes a first version. When something it reads contradicts them, it says so. The notes hold
intent only, never what the workflow already shows.

- **Answers questions about the open workflow.** Each message sends the workflow as the editor
  holds it at that moment, unsaved edits and test-run outputs included, with an outline, a
  dependency map and the list of saved versions. The answer names blocks as links; a link opens
  the block's details in a drawer. While the agent works, its steps show live under the message
  ("Searched the prompts for …", "Opened Revenue breakdown"), and **Stop** ends it.
- **Reads past reports as examples.** **Use reports as examples** opens **Choose reports**: the
  organization's past runs of this workflow, newest first, with search (**Search company, report or document**), and filters for **Workflow**, **Status**, **From** and **To**. The **Workflow** filter
  lists this workflow first and then every type the organization has access to. Up to 5 reports
  can be chosen; **Use these reports** adds them as chips above the message, and they go with the
  next message only. The agent also sees a list of the 30 latest runs and can open up to three per
  step on its own. A report it reads arrives whole: the executed run with every block's output, and
  its documents' page text.
- **Works on report feedback.** **Use feedback (7)**, beside **Use reports as examples**, shows how
  many [report feedback](report-feedback.md) items are open on this workflow's reports, and
  is hidden when none are. Pressing it opens **Choose feedback**, which lists the open items newest
  first with their comment, category, date and quoted text. The user ticks one, several, or
  **Choose all** (**Choose the newest 30** when more are open), up to 30, and **Use this feedback**
  attaches them to the next message: a chip such as "3 feedback items" shows above the message box,
  and an empty box fills with "Fix the feedback I chose on this workflow's reports.", which the user
  can change. Nothing is sent until **Send**. The message carries those items (the comment, the
  quoted text, the category and the report of each). It stages whole the reports holding the most
  chosen items, in the report slots the chosen examples leave
  (5 in all); the agent can open the others. The message shows a chip, such as "3 feedback items". A user
  sees their own organization's items; a super admin sees every organization's items on the
  workflow type, and the agent can open those reports, which stay within the deployment. The agent
  names the items each proposed change fixes; when the user saves an accepted change, those items
  become **Addressed** (see [Saving](#saving)). When the agent judges that an item is not something
  the workflow can fix, such as a wrong figure in the source document or a matter of taste, the
  answer lists it under "The agent suggests dismissing this feedback", with the reader's comment, the
  agent's reason and a **Dismiss** button. Nothing is dismissed until the user presses it; the
  reason is stored as the item's dismiss reason.
  Each row in **Choose feedback** also has a bin button, **Delete feedback**, which asks
  "Delete this feedback?" and, on **Delete**, removes the item for good. It reaches the items the
  dialog lists: the user's own organization's, or every organization's for a super admin. The
  row goes, and **Use feedback (n)** and the "n feedback items" chip drop the item, even when the
  user then presses **Cancel**. A message already sent the item keeps the copy staged for its run,
  and a later save that names it skips it.
- **Proposes changes.** When a change would help, or the user asks for one, the answer carries a
  review of the proposed changes, headed with what it touches ("Changes to 4 blocks in 2
  sections"). It opens with the agent's overview of the whole proposal in plain words, then groups
  the changes by section in workflow order, with one card per block (see
  [Reviewing a proposed change](#reviewing-a-proposed-change)). **Accept all** takes every change
  that can be accepted together. **Undo** takes back the accepted changes of that answer, as long
  as they are not saved. An accepted change lands in the editor at once, marked "Changed by the
  agent", and is not saved: the review says "Applied to the editor. Save to keep it."
- **Lets the user choose the model.** The menu beside **Use reports as examples** offers the
  platform's two default models, the ones every model call in the deployment uses: **Advanced**
  (the default) and **Standard**. Above them it lists **Expert**, Claude Opus 5.5 by default
  (`EditorAgent:ExpertModel`): the most capable and most expensive choice, for hard questions about
  large workflows, and never the default. Each is named with the model it is, what it is good at
  and its price per million tokens read and written. **Expert** is left out when the runtime
  allowlist has no form of it or when it is already one of the two default models. Both follow the deployment's settings
  (`Anthropic:AdvancedModel` and `Anthropic:BasicModel`) and the organization's override in
  `ModelRouting:TenantModelOverrides`, so moving the deployment's advanced model moves the agent
  with it. `Anthropic:AdvancedModelIdOverride` and `BasicModelIdOverride` apply too on direct-API
  and Azure deployments; AWS Bedrock deployments ignore them, as their model calls do. Each is offered in the form the runtime allowlist
  (`AgentRuntime:AllowedModels`) names it; one the allowlist lacks is left out and logged as a
  warning. A conversation already on another allowed model keeps it, listed under its own name.
  When the allowlist holds neither default, the menu lists every allowed model and marks the
  default **Recommended** (see `EditorAgent:DefaultModelMatch`). The server refuses a model the
  menu would not offer the organization, and **New conversation** always starts on the default.
- **Lets messages queue.** A message sent while an answer is still running waits ("Waiting to
  start. It starts when the answer above is finished.") and starts on its own; **Don't send**
  takes it out of the line. **New conversation** starts over.

### Reviewing a proposed change

The agent writes its proposal for the person reviewing it, who may be a client who does not know how
the workflow is built: an overview of two to four sentences, and one or two sentences per block or
section it changed, in the words the editor uses. The review shows them in this order:

- **Overview.** The agent's account of the whole proposal. When the agent wrote none, the review
  says what the proposal touches instead.
- **One group per section**, in workflow order, with **Accept section** and **Reject section** for
  everything in it, and **Undo section** once its changes are accepted and not yet saved. Accept
  section leaves out the changes that need their own review (the labels below). A large proposal
  starts with only its first two sections open.
- **One card per block**, marked **New**, **Changed**, **Moved here** or **Removed**, with the agent's
  sentence and every field it changes: instructions as the words added and removed, other settings
  as before and after. Model, effort and thinking always show together, as they are and as they
  would be. A new block shows its instructions; a removed block names the blocks that read its
  answer and would lose it. **Accept** and **Reject** act on the whole block; **Open block** shows
  the block as it is now.
- **A reorder card** only when the agent changed the order of a section's blocks, or of the
  sections, showing the order before and after. Numbers that shift only because a block was added,
  removed or moved are not shown and need no decision: they apply with the change that caused
  them, so an accepted new block lands where the agent put it.

**Review in full** spreads the review across the screen, with the cards side by side; **Narrow
view** goes back. Until a change is decided, the block or section it touches carries a dashed mark in
the editor, "The agent proposes a change" (a new block marks its section), so **Back to editor**
shows where the proposal lands. The editor's **Graph** tab draws the workflow as it would be with
those changes: a block the agent adds has a green dashed outline, a block it changes a blue one,
and **Agent's proposal** switches back to the workflow as it is. The agent's steps fold a step
repeated in a row into one line with its count ("Looked through the workflow ×6").

Before a change reaches the review, the platform runs every check a save runs on the changed
workflow, tries the changes the way the editor will apply them, and sends the agent back to fix what
fails, up to three times. It stops sooner when the agent sends back the same mistakes twice. A
change can still show:

| Label on the change | Meaning |
|---|---|
| **Outside address** | The change can alter what the workflow sends to an outside service (a web call, a search or an exchange-rate lookup), directly or through what feeds it. **Accept all** leaves it out; accept it on its own after checking the address. |
| **Library** | The block follows a Library component; after saving, this workflow's copy differs from the Library until someone reconciles it. **Accept all** leaves it out. |
| **Not listed** | The agent changed it without listing it in its summary. **Accept all** leaves it out. |
| **Cannot be accepted** | The change touches a Library link, still fails a save check after the agent's last attempt, or contains text from a client report. |

When the user edited the same field while the agent was working, the field shows both values and
offers **Keep mine** or **Use the agent's** instead. Accepting a block whose change sends data to an
outside address, from its own card, is the confirmation for that address. An agent block that would read a tag nothing
in the workflow supplies is listed under "Saving will ask you to confirm …", since the save asks
the same.

### Saving

The agent never saves. The user saves from the editor as usual. A save that carries accepted agent
changes stores a note on the new version: "3 changes proposed by the editor agent" (a change is one
block, section, list item or workflow setting; an order-only move does not count), followed by the
agent's overview of each proposal the save carries. The editor's version history shows it under
the version's summary. The note is counted once: a later save
does not count the same changes again, and a save that fails or changes nothing keeps them for the
next one. Once saved, the card shows **Saved.** and **Undo** goes.

The same save marks **Addressed** every open feedback item the agent named for a saved change, when
the conversation was sent that item with **Use feedback**. The item records the user who saved and
the new version (`addressedInVersionId`), so the report can be run again on that version to check
the fix. "Addressed" means a fix was saved, not that a re-run proved it.

## What it cannot do

- **It never saves or runs anything.** It proposes; every change waits for the user's review in
  the editor, and nothing is kept until the user saves.
- **It reads only the user's own organization's data**: the workflow staged with the message and
  that organization's past reports. A report of another organization, or of a workflow type the
  organization has no access to, is refused. The one exception is report feedback sent by a super
  admin: the agent may read the reports those items are on, in any organization.
- **It does not copy report content into the workflow.** The workflow is a template other
  organizations share. A change whose new text holds a passage of report text, a client or
  document name from a report, a confidential value, or three or more figures from a report that
  the workflow did not already hold is refused ("This change contains text from a client report"),
  and the agent is sent back to describe the pattern in general terms.
- **Conversations are private and kept for a limited time.** A conversation belongs to the user
  who started it, and ends 7 days (by default) after its last message, when it is deleted.

## Switching it on

Agent mode needs both `PlatformSettings:EditorAgent:Enabled` and `PlatformSettings:AgentRuntime:Enabled`
set to `true`. Either off hides **Work with the agent** and the panel; the shipped default is off. The
per-message spend, turn and time limits, the running-message cap per user, the default model and
the retention window are the `PlatformSettings:EditorAgent` keys in
[Application Configuration](../operations/application-configuration.md#editoragent).

## Cost

Every message is a paid run on the agent runtime, capped per message by
`EditorAgent:MaxBudgetUsdPerMessage` (3 USD by default). The panel's header shows the conversation's
spend so far ("This conversation: $0.42") and the organization's agent spend today against today's
allowance ("Your organization today: $3.10 of $50"). A message stopped at its deadline can learn its
full cost after it stops; the header updates when it does. The spend is the turn's tokens priced at list price, as for a workflow Agent block
([What an agent run reports to usage](../architecture/services/infrastructure/agent-runtime-integration.md#what-an-agent-run-reports-to-usage)),
recorded per organization and user as
`LlmEditorAgentChat` and shows on the usage dashboard as **Workflow Editor Agent**
([TokenUsages](../reference/database/user-data-database.md#tokenusages)). Stopping a message bills what
it spent until then.

### Daily spend per organization

Each organization has a daily batch of agent spend: `EditorAgent:DailySpendBatchUsd` (50 USD by
default), or the organization's own amount, which a super admin sets under **Administration >
Tenants > Edit tenant > Workflow agent daily spend (USD)**. Spend is counted per UTC day from the
settled `LlmEditorAgentChat` rows of every user in the organization, and each message still waiting
or running counts at its per-message cap until it settles.

Once today's spend reaches the batch, the next message is not sent. A box headed **Today's agent
spend is used** reads, for example, "Your organization's workflow agent spend today, counting
messages still running, is $52, past its daily $50. Continue with another $50? Your name is
recorded against it." **Unlock and send** adds enough batches to bring the day one batch above its
spend, records who unlocked them
([EditorAgentSpendUnlocks](../reference/database/user-data-database.md#editoragentspendunlocks)), and
sends the message; **Not now** keeps the message in the box. There is no limit on how many batches
a day may unlock. A message already running always finishes, and its settled cost can differ from
its cap, so the day can end a little past its batches.

## Related pages

- [Editor Agent Evals](editor-agent-evals.md): the eval set that checks its answers and proposals against a live runtime
- [Agent conversations](../architecture/services/infrastructure/agent-runtime-integration.md#agent-conversations): turns, waiting turns, the stop gate, report staging and the report-text check
- [UserData reference](../reference/database/user-data-database.md#agentconversations): the conversation, draft and proposal tables
- [Template Versioning](workflow-template-versioning.md): where the version note is stored
