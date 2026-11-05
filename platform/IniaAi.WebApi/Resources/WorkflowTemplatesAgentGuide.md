# Workflow Template Editing API — Agent Guide

You are an AI agent editing Inia workflow templates over HTTP. This guide is the contract:
read it fully before your first edit. Saves go live immediately — there is no draft stage —
so work carefully and confirm intent with your human operator before saving to a template
that is in production use.

## 1. Authentication

Obtain a token with the organization's service account (client credentials):

```
POST /api/v1/connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=service_user_<orgId>&client_secret=<secret>
```

Send on **every** subsequent request:

- `Authorization: Bearer <access_token>`
- `X-Tenant-Id: <orgId>`

The content endpoints require the `WorkflowEditor` role (a `SuperAdmin` token also passes). Service users do **not** have it by default — an Inia super admin grants it per organization in Admin → Roles before these endpoints will authorize. Until then the content endpoints return 403; the list and this guide also require the role, and only the summary endpoint stays accessible to any authenticated token.

## 2. Endpoints

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/api/v1/workflow-templates` | List all workflow types in this deployment (id, name, category, `hasContent`). |
| GET | `/api/v1/workflow-templates/{typeId}/content` | Full template JSON + `versionId` (your concurrency token). |
| PUT | `/api/v1/workflow-templates/{typeId}/content` | Full-document save. Upserts the type when it does not exist. |
| POST | `/api/v1/workflow-templates/{typeId}/content/validate` | Rehearse that save: same rejections, nothing written — see below. |
| GET | `/api/v1/workflow-templates/{typeId}/summary` | Lightweight summary (variables, document classes) — not for editing. |
| GET | `/api/v1/workflow-templates/{typeId}/document-classes` | List document classes + head `versionId` (your concurrency token). |
| GET | `/api/v1/workflow-templates/{typeId}/document-classes/{key}` | Read one document class. |
| POST | `/api/v1/workflow-templates/{typeId}/document-classes` | Create one document class. |
| PATCH | `/api/v1/workflow-templates/{typeId}/document-classes/{key}` | Update one class; omitted fields stay unchanged. |
| POST | `/api/v1/workflow-templates/{typeId}/document-classes/{key}/rename` | Rename a class; cascades the new key into every reference. |
| DELETE | `/api/v1/workflow-templates/{typeId}/document-classes/{key}` | Remove a class; consumers left classless fall back to `Any`, output references are cleared. |
| GET | `/api/v1/workflow-templates/{typeId}/versions` | Saved version history, newest first: who, when, narrative. See §9. |
| GET | `/api/v1/workflow-templates/{typeId}/versions/{versionId}/content` | One saved version's template JSON, same shape as `/content`. See §9. |
| GET | `/api/v1/workflow-templates/{typeId}/versions/{versionId}/changes` | What that version changed versus the version before it. See §9. |
| GET | `/api/v1/workflow-templates/{typeId}/versions/compare?from=&to=` | Diff between any two versions of the type. See §9. |
| POST | `/api/v1/workflow-templates/{typeId}/duplicate` | Copy the whole template into a new workflow type, links intact — see §7. |
| DELETE | `/api/v1/workflow-templates/{typeId}` | Delete a workflow type only your organization uses, with its runs — see §7. |
| POST | `/api/v1/workflow-templates/{typeId}/shared-components/promote` | Publish a template node to the component Library — see §6. |
| POST | `/api/v1/workflow-templates/{typeId}/shared-components/materialize` | Insert a Library component into the template — see §6. |
| GET | `/api/v1/shared-components/suggestions` | Find repeated content that belongs in the Library — see §6. |
| GET | `/api/v1/workflow-templates/{typeId}/shared-components/reconcile` | Classify every Library-linked node in the template — see §6. |
| POST | `/api/v1/workflow-templates/{typeId}/shared-components/reconcile/apply` | Apply per-node reconcile decisions atomically — see §6. |
| GET | `/api/v1/workflow-templates/{typeId}/export` | Export the template plus every shared component it depends on as a portable transfer bundle — see §8. |
| POST | `/api/v1/workflow-templates/{typeId}/import-preview` | Preview importing a bundle: how each element relates to this deployment's copies — see §8. |
| POST | `/api/v1/workflow-templates/{typeId}/import-apply` | Apply a bundle with per-element take-theirs / keep-mine decisions, atomically — see §8. |

### PUT request body

```json
{
  "workflow": { ...full template JSON exactly as returned by GET... },
  "baseVersionId": "<versionId from your GET>",
  "newWorkflowType": { "name": "...", "description": "...", "categoryId": "..." }
}
```

`newWorkflowType` is only used when creating a brand-new type; otherwise omit it. Valid
`categoryId` values for it are the workflow-type categories — read them from
`GET /api/v1/workflow-categories`, or reuse a `categoryId` already present in the template list.
`baseVersionId` is **required whenever the GET returned a non-null `versionId`**; pass null
only when `versionId` was null (first save of a new or legacy type).

### Responses you must handle

- **200** — saved. Body: `versionId` (new head), `contentHash`, `wasDeduped`,
  `createdWorkflowType`, and `warnings[]` (`path`, `message`). Read the warnings: an
  unresolved placeholder you introduced means your edit references a tag that does not exist,
  and a warning on `DocumentClasses` names blocks that search page overviews of classes whose
  pages get none, because neither the class nor the workflow default has an overview prompt.
- **400** — structurally invalid. The validation problem lists JSON paths
  (e.g. `Sections[2].Blocks[1].RagSettings`). Fix the payload; nothing was saved.
- **409** — your `baseVersionId` is stale: someone saved after your GET. The problem details
  include the current head info: `currentHeadVersionId`, `currentContentHash`, and
  `headCreatedAtUtc`. Re-GET the content, re-apply your edit to the fresh JSON, and PUT
  again with the new `baseVersionId`. Never retry with the old payload — you would discard the
  other writer's change.
- **404** — the type does not exist, or it was deleted.

### Rehearsing a save

```
POST /api/v1/workflow-templates/{typeId}/content/validate

{ ...exactly the body you would PUT... }
```

Send the payload you are about to save and find out how the save would answer, without writing
anything. The statuses are the save's own, so the handling you already wrote covers this: **400**
with the same field-level paths, **409** with the same `currentHeadVersionId` /
`currentContentHash` / `headCreatedAtUtc`, **200** when the payload would be accepted.

- The 200 body is `wouldCreateWorkflowType`, `headVersionId`, and `warnings[]`.
  **`wouldCreateWorkflowType`** earns the call whenever you are saving with `baseVersionId: null` —
  a first save, or a legacy template whose GET returned a null `versionId`. The PUT upserts, so
  there a mistyped `{typeId}` quietly creates a second workflow type instead of refusing. When you
  do hold a `baseVersionId`, a mistyped id already fails on its own: the type you named has no head,
  so it 409s.
- It is narrower than the save on purpose. It will not tell you whether the save would dedupe, or
  whether it would need `confirmSignatureFieldDeletions` — both are settled only after the template
  graph is replaced. Neither is dangerous to discover on the real save: a dedupe is a no-op, and an
  unconfirmed signature deletion is refused with nothing written.
- Advisory, like every preview here. The save re-runs all of it inside its own transaction, so a
  write that lands between your rehearsal and your save still turns an accepted payload into a 409.
  Rehearsing is not a substitute for sending `baseVersionId`.

### Editing a single document class

For a change scoped to one document class — add, rename, retitle, change requirement, or remove —
use the `document-classes` sub-resource instead of rewriting the whole template. For edits spanning
blocks or sections, use `GET`/`PUT /content`. Both operate on the **same template and the same
version head**, so the two paths interleave safely and a `versionId` from either read is your
concurrency token.

The loop mirrors the full-document save:

1. `GET .../document-classes` — you get the classes plus the head `versionId`.
2. Send that value back as `baseVersionId` on your write: in the JSON body for create, update, and
   rename; as a `?baseVersionId=` query parameter for delete. It is required whenever a head version
   exists; omit it only for a type with no version history yet.

```
PATCH /api/v1/workflow-templates/{typeId}/document-classes/invoice

{ "requirement": "required", "baseVersionId": "<versionId from the list>" }
```

Only the fields you send change; the rest are untouched. Rename is its own call (it rewrites every
block and parameterized-section reference), and updating never renames:

```
POST /api/v1/workflow-templates/{typeId}/document-classes/invoice/rename

{ "newKey": "invoice_v2", "baseVersionId": "<versionId>" }
```

Delete carries the token in the query string:

```
DELETE /api/v1/workflow-templates/{typeId}/document-classes/invoice?baseVersionId=<versionId>
```

Statuses specific to this sub-resource:

- **200 / 201** — applied. The body echoes the resulting `documentClass` plus the new `versionId`.
  Use it as the `baseVersionId` for your next write; no re-read needed.
- **409** — either your `baseVersionId` is stale (someone saved after your list; the problem details
  carry `currentHeadVersionId`, `currentContentHash`, `headCreatedAtUtc` — re-list and retry) or the
  key already exists (on create, or rename to a taken key — pick another key).
- **400** — invalid, or a reserved key (`Any`, `None`).

## 3. Understanding the template JSON

The `workflow` object is the verbatim PascalCase domain representation. Top-level shape:

```
{
  "Name": ..., "Title": ..., "Description": ...,
  "Sections":  [ { "Name", "Title", "Order", "Blocks": [ ... ] } ],
  "Variables": [ { "ReplacementTag", "DisplayName", "Type", ... } ],
  "DocumentClasses": [ { "Key", "DisplayName", "Requirement", ... } ],
  "Configuration": { ... }
}
```

Each block: `"Type"` (e.g. `AiQuery`, `Merge`, `WebApiCall`), `"ReplacementTag"` (its handle),
`"Name"`, `"Order"`, `"Properties"` (type-specific; prompts usually live in
`Properties.UserPromptTemplate`), `"TextInputs"`, `"RagSettings"`. A block or section may also
carry reserved `SharedComponent*` link fields — see the rules below.

**Orient before you edit.** Templates are large; print a table of contents first instead of
reading everything:

```python
import json
wf = json.load(open("content.json"))["workflow"]
for si, s in enumerate(wf.get("Sections", [])):
    print(f"{si:>2}. {s.get('Title') or s.get('Name')}")
    for bi, b in enumerate(s.get("Blocks", [])):
        print(f"    {si}.{bi}  [{b.get('Type','?'):<16}] {b.get('ReplacementTag','?'):<36} {b.get('Name','')}")
```

Serialization note: boolean fields whose value is false (e.g. a document class's
`IsOutputDocument` and `ShouldBeSingular`) are omitted from served JSON entirely — when diffing
old vs new payloads, treat an absent boolean as false rather than as a removed field.

Data flow: blocks reference other blocks' outputs via placeholders like
`{some_tag.answer}` inside prompts. The base tag before the first `.` must match a block
`ReplacementTag`, a variable `ReplacementTag`, a document class `Key`, or a reserved input
(`Counterparty`, `Organization`, `ReportDate`, ...). Unknown references come back as warnings.

### The `Agent` block (gated)

`"Type": "Agent"` runs a skill-driven agent on the external agent runtime. The type is
**save-gated per environment**: unless the deployment has the agent runtime enabled
(`PlatformSettings:AgentRuntime:Enabled` — off by default), any save containing an agent
block is rejected with a 400 naming the gate; treat the type as unavailable there. The gate
holds at **every** write that lands a template graph — the content PUT, shared-component
fan-out, materialize, reconcile apply, duplicate, and bundle import-apply all refuse rather
than land agent content on a disabled deployment — and at the definition mouths themselves:
creating a component, replacing its definition, or restoring a version whose payload carries
an agent block refuses with `SharedComponent.AgentRuntimeDisabled` (§6's code table). A
definition that places a verification block inside a parameterized or looped section refuses in
both flag states: with `SharedComponent.AgentExpansionScope` on an enabled runtime, because such a
definition could never land in any consumer, and folded into the `AgentRuntimeDisabled`
refusal when the runtime is off. An `Agent` block may sit inside a parameterized or looped
section: expansion rewrites the double-brace tags in its skills, task prompt and system prompt
per variant or iteration (`{{facts}}` becomes `{{facts_N}}` or `{{items_facts_0}}`, a parameter
becomes its value, the loop item `{{items}}` becomes `{{items_0}}`), and each copy buys its own
run. Still refused: an agent outside a parameterized section reading a producer inside it by its
template tag (read `tag_<variant key>`), an agent after a loop reading a field or an index of a
loop-body producer (only the whole output fans in, one line per iteration), and a verification
member inside a parameterized section. One loop may start at most
`PlatformSettings:AgentRuntime:MaxLoopAgentCopies` agent runs (default 20); a longer list fails
the run before any of them starts. The gate also holds at **submission**: a workflow that
contains an agent-family block is refused when the runtime is off —
`WorkflowExecution.AgentRuntimeDisabled` from the shared orchestrator behind the API submit
paths, and the same guard client-side behind the editor's Generate Report button — before any
execution row exists, so nothing is spent; the refusal names the blocks to remove. On an
enabled deployment a saved agent block runs on the agent runtime like any other block. Because
there is usually no sibling to clone (rule 3), this is the one `Properties` shape documented
here:

```json
{
  "Type": "Agent",
  "ReplacementTag": "extraction_agent",
  "Name": "Extraction agent",
  "Order": 0,
  "Properties": {
    "Type": "Agent",
    "WorkerSkills": [
      {
        "Name": "extraction",
        "Description": "Extracts line items from a financial statement. Use when the task asks for line items.",
        "Content": "# Skill markdown\nExtract the line items for {{client_info.answer}}."
      }
    ],
    "ShouldUseAdvancedModel": false
  }
}
```

- `WorkerSkills` — a list of skills; at least one entry, and every entry needs a non-blank
  `Name`, `Description`, and `Content` (400 otherwise).
- `Name` is the identity the model invokes the skill by, not a file name: lower-case words
  joined by single hyphens, no extension, at most 64 characters (`income-statement`). A name
  outside that shape is a 400, as are two names that reduce to the same identity.
- `Description` is what makes the skill reachable — the runtime writes it into the skill's
  frontmatter and the model reads it to decide whether to use the skill. Say what the skill
  does and when to use it, in at most 1400 characters. Do not put a `---` frontmatter block
  inside `Content`; the runtime composes one from `Name` and `Description`.
- `ShouldUseAdvancedModel` picks the model tier, as on an AiQuery block. `true` (the default,
  also when the field is absent) runs the deployment's advanced agent model, Opus 5.5 unless the
  deployment or the organization configures another; `false` runs its standard agent model,
  Sonnet 5.5 unless configured otherwise. Prefer the standard tier unless the task needs Opus.
- `Model` is optional and overrides the tier when set. It must be one of the environment's
  allowed models. Use the provider-neutral id (`claude-sonnet-5-5`, not
  `us.anthropic.claude-sonnet-5-5`). A provider-specific id of an allowed model is accepted and
  saved as the neutral id. Leave it out to let the tier decide. A rejected save's error message
  lists the allowed ids, and that message is also how you discover them. A save is also refused
  when the block leaves `Model` blank and its tier's model is not in the allowed list.
- Skill content is **exempt from the placeholder system**: single-brace text (JSON examples,
  prose braces, CI syntax) passes through saves untouched and never produces
  unknown-reference warnings. References inside skills use a strict double-brace grammar
  (`{{tag}}`), resolved only when the block executes on the agent runtime.
- `OutputFiles` is optional: a list of paths under `out/` such as `["out/rows.json"]`. Use it
  when the agent makes data with a script and the data must reach later blocks exactly as the
  script wrote it. The block output becomes
  `{"answer": <the agent's answer>, "files": {"rows": <the JSON in out/rows.json>}}`, so later
  blocks read `{extraction_agent.answer}` and `{extraction_agent.files.rows}`. The answer is
  embedded as JSON when it is valid JSON, otherwise as a string. The platform adds a line to
  the agent's task naming the files to write. Each path is `out/`, optional folders, then a
  name ending in `.json`, using letters, digits, `_` and `-`. Two paths with the same file name
  are a 400. The block fails if a listed file is missing or is not valid JSON, or if the
  combined output is over 8 MiB. `OutputSchema` still checks only the answer; set one when later
  blocks read fields of the answer, since an answer that is not valid JSON arrives as a string.
  Turning the field on for an existing block moves its answer under `answer`, so update the tags
  that read it. Leave the field out to keep the output as the answer alone.
- **Do not describe the run in the prompts.** Every Agent and Verification run's system prompt
  starts with two sections the author never writes, delimited by XML tags so they never clash
  with markdown headings in a prompt: the runtime's `<environment>` (tools, working directory,
  `docs/`, `out/`, how to invoke a skill), and the platform's `<task_setup>`, which lists the
  skills as `skills:<name>`, the skill files as `$SKILL_ASSETS_DIR/<skill>/<file>`, and a line
  saying the final answer is checked against an enforced output schema when the block has one
  (each line only when it applies; no `<task_setup>` when none does). The authored system prompt
  follows, unchanged, inside `<instructions>`. The runtime states every staged document with its
  document class in the first user turn.
  Leave all of that out of `SystemPromptTemplate`, `PromptTemplate` and skill content; say only
  what the task needs, such as when to use each skill.

### The `Verification` block (gated)

`"Type": "Verification"` declares a **verified group**: a verifier that judges a named set of
Agent blocks against engineer-declared criteria and, when it fails one, sends that member back
to redo its work. It never rewrites a member's output, and it deliberately carries **no output
schema** — the verdict envelope is system-owned, so no authored property may redefine it.

The type is **save-gated on the same flag as the `Agent` block**
(`PlatformSettings:AgentRuntime:Enabled`): the two are the *agent family*, and on a disabled
deployment a save containing either is rejected with a 400 naming the gate and the agent-family
blocks it found. Everything the agent gate above says about where it holds applies here too —
every write that lands a template graph, and the Library's definition mouths — and a
verification block inside a parameterized or looped section is refused, because it names its
members by tag and settles one verdict for them, and expansion would copy it. At those definition mouths a verification block that breaks one of the rules
below refuses as `SharedComponent.AgentBlockInvalid` (§6's code table).

**Runnable.** A submission carrying a verification block runs like any other on a deployment
where the agent runtime is enabled: the loop verifies the members, sends the failed ones back
to redo their work, and re-verifies, within `PassCap`. The one pre-spend refusal is the
agent-family submission gate above — on a disabled deployment the workflow is refused before an
execution row exists, verification block included.

**Paid build only.** Only the paid build runs a verification block. A deployment built from the
open source keeps the block in its templates with its settings, and saves it without the paid
build's member, criterion, pass-cap and fuse rules. A run there fails the block with a message that the open
build does not include it, and its members run as plain Agent blocks whose output reaches the
report.


**The rehearsal endpoint does not answer these gates.** `POST .../content/validate` (§2) runs
the structural rules only; every agent-family gate above runs inside the save itself, so a
payload the rehearsal accepts can still come back a 400 from the PUT. That refusal is free —
nothing is written — but do not read a clean rehearsal as a verification block that will save.

## 4. Editing rules

1. **Edit as data, never as text.** Load the JSON in Python and mutate the exact node. Never
   string-replace or regex the raw payload — the same prompt fragment can occur in several blocks.
2. **Target blocks precisely.** `ReplacementTag` may legitimately repeat across sections.
   Match on tag + name + type (or section index) and assert you found the node you expected.
3. **New blocks: clone a sibling.** Copy an existing block of the same `Type`, then change ids,
   tag, name, and prompts. Do not hand-build the dict or improvise property keys — `Properties`
   shapes are type-specific and not documented here (the gated `Agent` and `Verification`
   blocks, which usually have no sibling to clone, are the exceptions — see their subsections
   in section 3). If the sibling
   carries `SharedComponent*` fields, STRIP them from your copy unless you deliberately intend
   another live copy of that Library component (see rule 9).
4. **Never set a collection to `null`.** `Sections`, `Blocks`, `Variables`, `DocumentClasses`,
   `RagSettings`, `TextInputs`, `SupportedDocumentClasses` must be arrays or absent. Explicit
   `null` is rejected with a 400.
4a. **Every block must keep a non-null `Properties` object.** Never set `Properties` to `null` or
   omit it — a block without `Properties` is rejected with a 400. When adding a block, clone a
   sibling's `Properties` (rule 3) rather than leaving it empty.
5. **Keep `ReplacementTag` unique within a section.** The server accepts a duplicate — same
   section or across sections — with only a warning, because references resolve to the first block
   that produced a response. Do not rely on a 400 to catch a tag collision: read the warnings and
   fix any collision your edit introduced.
6. **`Order` controls execution order**, not array position. If you reorder or insert blocks,
   renumber `Order` consistently. Negative and duplicate `Order` values are warned and normalized
   on save; other anomalies (gaps, out-of-range values) are silently renumbered by rank.
7. **Preserve everything you did not mean to change.** Do not reformat, re-sort keys, or touch
   unrelated fields. The server deduplicates truly identical saves — an accidental no-op save
   returns `wasDeduped: true`, which is your signal that nothing actually changed.
8. **Stay anchored to the requested change.** Do not sprawl into unrelated cleanups.
9. **`SharedComponent*` fields are reserved link metadata — never write them.** A block or
   section may carry `SharedComponentId`, `SharedComponentLinkState` (`"Linked"` or
   `"BrokenOut"`), `SharedComponentVersionHash`, and `SharedComponentBindings`. They mark the
   node as a live copy of a shared Library component and are stamped exclusively by the
   shared-components endpoints (§6):
   - Never add, edit, or invent them. A fabricated or copied-in id is rejected with a 400 naming
     the exact JSON path (`SharedComponent.UnknownComponent`); a malformed field is rejected as
     `SharedComponent.InvalidLinkShape`.
   - Round-trip them **verbatim** when present. Absence means a plain local block — the normal
     case. One exception rides along: a linked section may come back carrying an `Entries` array.
     That is definition vocabulary, not template content — the save drops it and reports a warning,
     so you can PUT the body back exactly as you read it. Do not hand-author `Entries`; a section's
     slots are described by its component's own definition (§6).
   - Editing the *content* of a node that carries them is allowed: your copy diverges locally
     (`LocalAhead` in the reconcile read), and the reconcile endpoints (§6) then let you push
     your edit to the Library, adopt the head back, or break the link.
   - A save that changes only these fields produces a new `versionId` with an **unchanged**
     `contentHash` — `contentHash` tracks template content, not link metadata, so equal hashes do
     not mean "nothing happened"; `wasDeduped: true` is the only no-op signal (rule 7).

## 5. Recommended loop

1. GET the content; save it to a file; print the table of contents.
2. Locate the target block(s); read them in depth.
3. State your plan in one short paragraph to your operator; wait for a go-ahead if the
   template is production-facing.
4. Apply the edit in Python; diff old vs new JSON and verify only the intended paths changed.
5. When saving with `baseVersionId: null`, POST the payload to `/content/validate` first and confirm
   `wouldCreateWorkflowType` matches your intent — that is the case where the PUT upserts a mistyped
   id instead of refusing it.
6. PUT with `baseVersionId`; on 409 re-fetch and re-apply; on 400 fix and retry.
7. Report the new `versionId` and summarize the warnings, if any.

Every save is snapshotted server-side with an auto-generated change narrative, so a human can
always review history and restore a previous version — but do not rely on that as an undo
button; get the edit right. §9 covers reading that history, and restoring from it, over this API.

## 6. Shared components (Library)

Reusable blocks and sections live in a deployment-wide registry — the Library. A template node
links to a component through the reserved `SharedComponent*` fields (rule 9). You never write
those fields yourself; every link is created, moved, or severed exclusively by the endpoints in
this section.

The registry surface, on `/api/v1/shared-components`:

| Method | Route | Purpose |
| ------ | ----- | ------- |
| GET | `/shared-components` | List components (id, kind, name, state, category, head tokens, `consumerCount`, `consumingWorkflowTypes` with per-type linked-copy counts, `viaSectionConsumerCount` for the copies reached through section ref entries, `brokenOutInstanceCount` for the copies that forked). Filters below. |
| POST | `/shared-components` | Create a new component with its first version from a definition (`kind`, `name`, required `categoryId`, optional `description`, and the definition JSON). Re-normalized and ref-validated server-side; no template anchor and no fan-out — a fresh component has no consumers. |
| GET | `/shared-components/suggestions` | Sweep every template for repeated content that is not in the Library yet, and for content that already matches a component. See below. |
| GET | `/shared-components/{id}` | One component's registry entry. |
| GET | `/shared-components/{id}/consumers` | The where-used list: workflow types holding live linked copies, with per-type counts and categories. Each row also carries `channel` — `Direct` when the type links this component itself, or `ViaSection` when it reaches it through a section component, in which case `viaSharedComponentId`/`viaSharedComponentName` name that section. A type can appear under both channels. |
| GET | `/shared-components/{id}/consumer-matrix` | Section components only: the consumers×entries grid — one row per live linked copy, one column per definition entry. A block component is refused with 400 `SharedComponent.KindMismatch` rather than an empty grid. See below. |
| GET | `/shared-components/{id}/content` | The head definition JSON plus `headVersionId` and `headContentHash`. 404 when the head's payload is not stored; 409 when the chain has no versions at all. |
| GET | `/shared-components/{id}/versions` | Version history, newest first: `versionId`, `contentHash`, `parentVersionId`, `createdAtUtc`, `createdByUserId`, `createdByName`, `payloadAbsent`. `?includeNarratives=true` adds a plain-language change summary per entry (needs stored payloads on both sides of each pair; the first version reads "First saved version"). |
| GET | `/shared-components/{id}/versions/{versionId}/content` | One stored version's definition JSON plus its tokens. 404 when the version's payload is not stored. |
| PUT | `/shared-components/{id}/content` | Replace the definition: the new head plus an atomic rewrite of every linked copy. |
| POST | `/shared-components/{id}/reconcile-preview` | Dry-run the impact of a definition change; optional draft definition in the body. |
| POST | `/shared-components/{id}/versions/{versionId}/restore` | Token-only restore: the version's stored payload becomes the head again, fan-out included. |
| POST | `/shared-components/{id}/retire` | Freeze the component; break out every linked copy and inline every definition-level ref to it, atomically. |
| POST | `/shared-components/{id}/unretire` | Reactivate a retired component; state-only. |
| GET | `/shared-components/categories` | The curated category vocabulary with per-category component counts, plus the uncategorized count. |
| PUT | `/shared-components/categories/{categoryId}` | Create or update a vocabulary entry. Ids are stable kebab-case (lowercase letters, digits, single dashes). |
| DELETE | `/shared-components/categories/{categoryId}` | Delete a vocabulary entry; refused with a 409 while components still reference it. |
| PUT | `/shared-components/{id}/category` | Re-file a component under a different category (`{"categoryId": "..."}` — must name an existing entry). |

The template-side operations live under `/api/v1/workflow-templates/{typeId}/shared-components`
(also listed in §2): `promote`, `materialize`, `reconcile` (GET), and `reconcile/apply`.

**List filters** — `?contentHash=<64 lowercase hex>` (exact match on the head content hash;
malformed → 400), `?categoryId=` (uncategorized components never match), `?workflowTypeId=`
(only components with a live linked copy in that template), `?workflowTypeCategoryId=` (only
components consumed by templates filed under that workflow-type category). Filters combine with
AND. A blank or omitted parameter is a no-op — it never means "match nothing".

### Concurrency tokens

Template-side writes (PUT `/content`, promote, materialize, reconcile apply) take the
**template's** `baseVersionId`, exactly as in §2 — stale means a 409 with the current head in
the problem details; re-read, re-apply, retry. Component writes (PUT component content, restore,
and the `UpdateShared`/`AdoptHead`/`Relink` decisions inside an apply) additionally take the
**component's** `expectedHeadVersionId` — the head you read and decided against; stale means a
409 `SharedComponent.HeadMoved`. Two situational variants of the component token exist, both
409ing `SharedComponent.HeadMoved` (the problem details name the component whose head moved):
`expectedRefHeadVersionId` on an entry-scoped `UpdateShared`/`AdoptHead` of a ref entry — the
**referenced Block component's** head, alongside the section component's own
`expectedHeadVersionId` — and `expectedContainerHeadVersionId` on a promote that converts a slot
of a Linked section copy — the **containing section component's** head.

Fan-out is the part that surprises: any component write rewrites **every** linked consumer
template in the same transaction, advancing their heads. The response's `consumerSaves[]` /
`workflowSaves[]` carry each rewritten template's new `versionId` — for any template you are
mid-edit on, adopt that as your new `baseVersionId` (or re-GET) before your next save. A
template 409 right after someone's Library push is the system working, not an error to retry
blindly.

### Addressing template nodes

Promote — and every reconcile decision below — targets a node by **exactly one** of:

- `targetPath` — the node's canonical path: `Sections[i]` for a section, `Sections[i].Blocks[j]`
  for a block. Indices are positions with sections and blocks ordered by `Order` — the order the
  content GET returns them in, and the same grammar 400 validation errors use.
- `targetStableId` — the node's `StableBlockId`/`StableSectionId` as served by the content GET.
  Prefer it over `targetPath` when you have the node's JSON in hand: a stable id keeps addressing
  the same node after reorders, while a path is only valid against the head you read it from.
  Round-trip stable ids verbatim; never invent or edit one. A template never re-saved since
  stable-id stamping was introduced carries no stable ids at all — when the content GET shows
  none, address by `targetPath`; your first save stamps them.

### Promote

```
POST /api/v1/workflow-templates/{typeId}/shared-components/promote

{ "targetPath": "Sections[2].Blocks[1]", "name": "Counterparty check",
  "description": "...", "baseVersionId": "<current head versionId>" }
```

- Creates the component (version 1 = the node's normalized definition) and stamps the node's
  link fields, atomically with a template save. The response carries `sharedComponentId`,
  `componentVersionHash`, and the template's new head tokens (`versionId`, `contentHash`,
  `wasDeduped`). A create-shaped promote **requires** `categoryId` naming an existing curated
  category (list the vocabulary to pick one); an attach never takes it.
- The link-only stamp leaves the template's `contentHash` **unchanged** while producing a new
  `versionId` (rule 9's last bullet).
- **Attach instead of create:** pass `attachToComponentId` and no `name`/`description`. The
  node's normalized content must match the component's head exactly — a mismatch is a 409
  `SharedComponent.ContentMismatch` with `expectedHash`/`actualHash` in the problem details.
  To find attach candidates, use the suggestions scan below — it reports the match directly. The
  `?contentHash=` filter on `GET /shared-components` does the same lookup, but only if you already
  hold the node's **normalized** hash, which is computed server-side and is not the hash of the JSON
  you hold.
- Rejections: node already linked → 409 `SharedComponent.AlreadyShared` (existing component id
  in the problem details); separator sections, expanded parameterized sections and the blocks
  inside them, and sections with empty or duplicate inner `ReplacementTag`s → 400 with the
  exact reason.
- Promoting a section whose inner blocks are Linked copies of Block components records those
  slots as floating references to the Block component (always tracking its head) instead of
  baking their content in; broken-out inner copies are baked in verbatim.
- **Promoting a block inside a Linked section copy** takes one of two shapes. A block the
  definition does not provide — a local insert, including a namesake block occupying a slot this
  copy has hidden — promotes like any other block; the section component is untouched. A block
  filling a definition slot instead **converts that slot**: the new Block component is built from
  the container definition's slot content, the slot flips from inline to a reference to it, and
  the container append fans out to every consumer of the section component (their copies are
  restamped content-identically). This shape requires `expectedContainerHeadVersionId` — stale
  or missing is a 409 `SharedComponent.HeadMoved` naming the section component. The slot's
  document-class wiring travels into the Block component, so wiring never blocks a convert. It is
  refused when the slot is broken out in this copy or carries a recorded per-entry override (400
  `SharedComponent.InvalidLinkShape`), and when your block's content has drifted from the
  definition slot (409 `SharedComponent.ContentMismatch` — reconcile the drift first).

### Finding what to promote

```
GET /api/v1/shared-components/suggestions
```

Sweeps every template in the deployment for content that repeats verbatim but is not in the Library
yet — the bootstrap path for templates that were hand-copied before the Library existed, and the way
to answer "does this already exist as a component?" without holding a normalized hash. Nothing is
written.

- A group is reported when the same normalized content sits in **two or more** places, or when it
  matches an **active component's head** even once. Matching is exact — near-duplicates are never
  suggested, so a group is always safe to collapse.
- Each group carries `kind`, `contentHash`, `suggestedName`, `crossType`, and `nodes[]`; each node
  carries `workflowTypeId`, `targetStableId` (prefer it) or `targetPath`, and its location for
  display. A node that was broken out of a component is a candidate again and names its origin in
  `provenanceComponentId`.
- **`existingComponentId` decides the shape.** Set → the content is already a component; *attach*
  every node you accept to it, and promote nothing. Absent → *promote* one node to create the
  component, then attach the rest to the `sharedComponentId` that promote returns.
- Attach calls omit `name`, `description`, and `categoryId` — sending any of them alongside
  `attachToComponentId` is rejected. A create-shaped promote **requires `categoryId`**; `name` is
  optional and falls back to a server-derived default, so pass `suggestedName` (or something
  better) when you care what the component is called.
- Each promote/attach needs that template's current `baseVersionId`. Take it from
  `GET /workflow-templates/{typeId}/content`, and when a group has two nodes in the **same** template,
  carry the `versionId` from the first call's response into the second — your own write moved that
  head.
- Blocks inside an actively Linked section never appear: they belong to that section's component.
  Actively Linked nodes are excluded entirely, so a scan run twice in a row shrinks as you work.
- It reads every template's full graph. `scannedWorkflowTypeCount` and `elapsedMilliseconds` report
  what that cost — run it when you intend to act on the result, not on a loop. Reserved deployment
  pseudo-types are skipped, exactly as `GET /workflow-templates` skips them, so every node names a
  template you can actually read and save.

### Materialize

```
POST /api/v1/workflow-templates/{typeId}/shared-components/materialize

{ "sharedComponentId": "<id>", "targetSectionPath": "Sections[0]",
  "index": 2, "replacementTag": "my_tag", "baseVersionId": "<current head versionId>" }
```

- A **block** component needs a target section (`targetSectionPath` XOR `targetSectionStableId`);
  a **section** component takes neither. `index` positions the landed node (default: append).
- The target may be a plain content section or a **Linked section copy** — landing inside a
  linked copy is a local insert of that copy: the landed block carries its own Library link, the
  section stays linked, and its definition is untouched (the reconcile read shows the block as a
  `LocalInsert` entry). Separators and expanded parameterized variants are 400s.
- The server performs the copy end to end: it resolves the instance tag (`replacementTag` if
  free — a collision is a 400, and the check is **template-wide** (stricter than rule 5's section
  scope) and case-sensitive — otherwise an auto-slug of the component name), renames colliding
  inner slot tags and records those renames in `SharedComponentBindings` (absent key = unchanged),
  and stamps the link fields. The response carries `landedPath`, `landedReplacementTag`,
  `bindings`, the new template head tokens, and `warnings[]` — read them: the definition may
  reference tags or document classes this template lacks.
- Unknown component → 404; a `Retired` component or a head without a stored definition → 409.
- An **agent-family component** materializes like any other when the runtime is on. A skill
  tag no producer in the target template supplies does not stop it: the component lands and
  `warnings[]` names the tag, which reads as no data at run time until you add its producer. On a
  runtime-disabled deployment the landed graph refuses with the gate error instead.

After either POST, treat the returned `versionId` as your new template concurrency token and
re-GET the content before further edits: the server-side stamp changed the template.

### The reconcile loop

`GET /workflow-templates/{typeId}/shared-components/reconcile` is a pure read: one row per node
carrying link fields, classified against the live registry. Each row gives both address forms
(`targetPath`, `targetStableId`), the component's name and state, `linkState` (`"Linked"` or
`"BrokenOut"`), the instance/link/head content-hash triple (plus `linkHashStale` when the stamp
lags a content-identical head), a `relation`, and `availableActions` — the sanctioned decisions
for that row, ordered default-first. Linked section rows additionally carry `entries[]`,
`orderDeviates`, `hasLocalOverrides`, `localInsertCount`, and `oldDefinitionAvailable` (see
"Composable sections" below); entry deviations never change the section row's own `relation`.
The response's `baseVersionId` is the anchor token an apply must echo.

| `relation` | Meaning |
| ---------- | ------- |
| `InSync` | The copy's content equals the component head. |
| `LocalAhead` | The copy was edited locally; the head has not moved. |
| `HeadAhead` | The head moved on; the copy still matches the version it was stamped from. |
| `Diverged` | Both the copy and the head changed since they last agreed. |
| `ComponentMissing` | The link's component id resolves to nothing in this deployment. |
| `ComponentRetired` | The component is retired; its definition is frozen. |

| `action` | Effect |
| -------- | ------ |
| `UpdateShared` | Push this instance's content as the component's new head and rewrite every other linked copy — including templates your request never mentioned. |
| `AdoptHead` | Rewrite this instance to the component head; on a broken-out copy this also relinks it. |
| `BreakOut` | Fork: the copy keeps its content and provenance but stops following the head. |
| `KeepLocal` | Explicitly leave the instance as it is. |
| `Relink` | Re-attach a broken-out copy whose content still matches the head (a mismatch is a 409 `SharedComponent.ContentMismatch`; for sections the match cancels per-entry overrides first). Relinking a section clears its per-entry overrides: hidden entries are restored from the head, broken-out entries reconnect to their referenced component's current head, and local-insert blocks survive untouched. Never entry-scoped. |
| `Hide` | Entry-scoped only: mark the slot `Hidden` on this copy — any live block for it (and its binding) is removed, and fan-outs stop materializing the slot here. Offered when the slot's block is already gone, to record the deletion as deliberate. |
| `Unhide` | Entry-scoped only: clear a `Hidden` override and re-materialize the slot from the component's current head (a ref entry hydrates from the referenced component's head; a taken tag lands auto-slugged). |

`POST /workflow-templates/{typeId}/shared-components/reconcile/apply` applies a batch:

```
{ "baseVersionId": "<the reconcile read's anchor>",
  "decisions": [
    { "sharedComponentId": "<id>", "targetStableId": "<sid>", "action": "UpdateShared",
      "expectedHeadVersionId": "<component head from the read>",
      "acknowledgeSignatureOrphans": false } ] }
```

- Everything commits in one transaction or nothing does.
- `expectedHeadVersionId` is required on `UpdateShared`, `AdoptHead`, and `Relink` — echo the
  component head the reconcile read showed you.
- `availableActions` is what the reconcile read *suggests* for a row, not the set the server
  enforces — it can accept a sanctioned action the read did not list (a present entry still takes
  `Hide`, for example). Prefer what it offered; the server re-validates independently and refuses
  a decision that addresses a node which is not a copy of its claimed component.
- Decisions that contradict each other are a 400 `SharedComponent.ConflictingDecisions`: two
  decisions on one node or one entry, a whole-section decision other than `KeepLocal` combined
  with entry decisions on the same section, an `UpdateShared` plus a `KeepLocal` on the same
  component, or multiple `UpdateShared` whose instance content differs (identical content is
  allowed — only one definition can become the head).
- The response carries `decisionOutcomes[]` (`alreadyCurrent: true` when an `UpdateShared`
  found the derived definition already at the head), `workflowSaves[]` (the anchor template plus
  every fanned-out consumer, each with its new `versionId`), and `componentHeads[]`.

### Composable sections: entries of a linked copy

A section definition is a list of **entries**: `Inline` slots carrying their block content in
the definition, and `ComponentRef` slots referencing an independent Block component and always
tracking its head. Refs arrive by promoting a section whose inner blocks are Linked copies, or
by the slot-converting promote above.

On the reconcile read, every Linked section row carries `entries[]` — the definition's slots in
order, plus this copy's local inserts — and `orderDeviates` (true when the copy's live block
order differs from the definition's entry order; informational). Each entry gives:

- `entryKey` (the definition slot tag; null for local inserts) and `entryStableId`/`entryPath`
  (the live block; null for hidden entries), plus `displayName` (the live block's label).
  Entry-scoped decisions address by exactly one of `entryKey` or `entryStableId`; a decision
  with neither is whole-section.
- `insertLinkState` — for local inserts only: the inserted block's own link state (`"Linked"`
  when it is a live copy of a Block component, `"BrokenOut"` after it forked, null for a plain
  local block). Null on definition-owned entries.
- `origin` — `DefinitionInline`, `DefinitionRef`, or `LocalInsert` — and `overrideState` —
  `None`, `Hidden` (this copy removed the slot), or `BrokenOut` (this entry stopped following
  the definition).
- For ref entries and Library-linked local inserts: the referenced Block component
  (`refSharedComponentId`, `refComponentName`, `refComponentState`) and its head tokens
  (`refHeadVersionId`, `refHeadContentHash`) — `refHeadVersionId` is what an entry-scoped push
  or adopt echoes as `expectedRefHeadVersionId`.
- The entry's `relation` against its definition counterpart (null for local inserts and hidden
  entries), `instanceContentHash`, `deletedByDefinition` (the definition dropped the slot while
  the local block survives), and its own `availableActions`, default-first.

Entry-scoped decisions ride the same apply batch with the entry locator added:

```
{ "sharedComponentId": "<section component>", "targetStableId": "<section sid>",
  "entryKey": "kyc", "action": "UpdateShared",
  "expectedHeadVersionId": "<section component head>",
  "expectedRefHeadVersionId": "<referenced Block component head — ref entries only>" }
```

- An entry-scoped `UpdateShared` on a **ref entry** (or a Library-linked local insert) pushes
  the live block's content as the **referenced Block component's** new head and fans out to that
  component's consumers — echo `expectedRefHeadVersionId`. On an **inline entry** it appends the
  section component's head with that entry's live content merged in; on a plain **local insert**
  the block joins the definition as a new inline slot. Multiple inline-entry pushes on one
  section merge into a single append.
- An entry-scoped `AdoptHead` rewrites just that entry from the definition (re-materializing it
  if the block is missing); a ref entry grafts from the referenced component's head and takes
  `expectedRefHeadVersionId` too. An entry-scoped `BreakOut` marks an inline entry `BrokenOut`;
  on a ref entry it breaks out the inner block's own Library link instead.
- `Hide`/`Unhide` are the entry override pair (table above). Hidden and broken-out entries
  refuse the arms that no longer apply — submit only what the entry's `availableActions`
  offered. Both are idempotent.

`PUT /shared-components/{id}/content` replaces the definition wholesale:

```
{ "definition": { ...a block, or a section with inner blocks, matching the component's kind... },
  "expectedHeadVersionId": "<head from your read>", "acknowledgeSignatureOrphans": false }
```

- Submit the shape the content GET serves. The server re-normalizes: per-consumer fields (a
  block's tag and order; a section's order) and any link fields are stripped before storing.
  Document-class wiring is **shared content**, not a per-consumer field: `SupportedDocumentClasses`
  stays in the definition, counts toward the content hash, and lands on every linked copy when the
  head changes.
- A definition byte-identical to the head is a dedup no-op: `wasDeduped: true`, nothing fanned
  out.
- Otherwise the new head rewrites every linked copy atomically — read `consumerSaves[]` and mind
  the token rules above. Each consumer save carries its own `warnings[]` for the rewritten
  template; a skill tag no producer in that consumer supplies surfaces there rather than
  refusing the push — the push-side counterpart of materialize's unknown-tag refusal.

Preview before you write: `POST /shared-components/{id}/reconcile-preview` with
`{ "candidateDefinition": ... }` returns the consumers, the regression baselines a fan-out would
stale, the override rows the change would orphan, and — for sections — the `slotDelta` against
the head: matched, added, and removed slot tags, `renamedSlots` (equal content under a new
tag), and `rescuedSlots[]` — pairs the server still correlated despite differing tag and
content, each with `oldTag`, `newTag`, and a `reason` (`NameMatch` or
`PositionAndTagSimilarity`); rescued blocks survive and take the new content. With no body it
reports just the blast radius every definition change shares. Previews are advisory; the writes
re-compute everything inside their transactions.

`POST /shared-components/{id}/versions/{versionId}/restore` makes a stored version the head
again without round-tripping content — the server re-appends its own stored payload:

```
{ "expectedHeadVersionId": "<current head>", "acknowledgeSignatureOrphans": false }
```

Restoring the current head is a dedup no-op; restoring an older version appends a new head that
repeats that version's content hash. A version flagged `payloadAbsent` cannot be restored (409).

### The consumers×entries matrix

`GET /shared-components/{id}/consumer-matrix` answers "which copies deviate, and where" for a
**section** component in one read. Block components have no entries and return empty lists.

**Columns** (`entries[]`) are the head definition's entries in definition order — `slotTag`, `kind`
(`Inline` or `ComponentRef`, with `refSharedComponentId` for the latter) — followed by any slot a
consumer filled locally, flagged `isLocalOnly: true`.

**Rows** (`rows[]`) are the live linked copies. One row per **copy**, not per type: a workflow type
holding two copies of the component appears twice, told apart by `stableSectionId`. Broken-out
copies are not rows — they stopped following the head.

**Cells** carry one `state` each, and they are field reads rather than content comparisons. A
persisted Linked copy is always at the component's current version, so a linked entry cannot have
silently drifted; deviation only exists as a recorded slot state or an absent block.

| State | Meaning |
| ----- | ------- |
| `Live` | The copy carries the definition's content and follows the head. |
| `Custom` | That entry broke out — it keeps its own content and no longer follows the head. |
| `Hidden` | The entry is hidden in this copy. |
| `Added` | A block this copy inserted locally, in a slot the definition does not own. |
| `Missing` | The definition owns the entry but this copy has no block for it. |
| `NotApplicable` | A local-only column another copy introduced; this copy has nothing to say about it. |

Read it before a definition change to see who will be affected and who has already diverged: a
`Custom` or `Hidden` cell will not receive your edit, and a column that is `Missing` across many
consumers usually means the entry was removed downstream rather than that the copies are broken.

### Retire and unretire

`POST /shared-components/{id}/retire` freezes the component and breaks out **all** linked copies
in one transaction — a retired component has zero linked consumers by construction; every copy
keeps its content and provenance. Retiring a **block** component also erases every
definition-level reference to it in the same transaction: each section component whose head
carries a ref entry to it gets a new version with that head content baked in as an inline slot
(entry bindings applied, order kept), and the containers' linked instances are restamped to the
rewritten heads — content-identical, so they stay `InSync`. The response reports
`brokenOutInstanceCount`, the `workflowSaves[]` the break-outs and restamps produced, and the
rewritten containers in `inlinedContainerIds[]`. `POST /shared-components/{id}/unretire`
reactivates state-only: copies broken out by the retirement stay broken out and inlined
definitions stay inlined; new consumers arrive by materializing again or via `Relink`. Both
calls are idempotent no-ops when the component is already in the target state. While retired, definition writes and new links (PUT content,
restore, materialize, `UpdateShared`/`AdoptHead` on its copies) are refused with a 409
`SharedComponent.Retired`; the reconcile read's `availableActions` reflects what remains.

### Orphaned signature overrides

Client organizations hold per-instance signature override values keyed to blocks inside linked
copies. A definition change that removes a slot deletes those rows with it; one that merely
drops a placeholder leaves them inert. Any write that would do either is refused with a 409
`SharedComponent.OrphansNeedAcknowledgement`; the problem details carry the affected rows under
`orphanedOverrides` (organization, workflow type, block, placeholder, and whether the row is
deleted or goes inert). This is a self-correcting loop: review the list — with your operator if
the values belong to a client — then re-send the identical request with
`acknowledgeSignatureOrphans: true`. Never send `true` pre-emptively: the refusal is the moment
the impact is disclosed, and it is re-computed inside the write transaction, so the list you
acknowledge is the list that applies.

The same loop guards a second kind of collateral. Fanning out a new head replaces content in every
linked copy, and a copy may hold local edits — the `LocalAhead` state whose `availableActions`
include `KeepLocal` — that the update would silently discard, in templates you may not be editing.
The gate covers every linked shape: an edited instance of a Block component, an edited inline entry
inside a linked section copy, and an edited referenced entry that grafting re-materializes from its
Block component's head. Such a write is refused with a 409 `SharedComponent.OverwritesLocalEntries`
naming the `workflowTypeId` and the `overwrittenEntrySlots` (entry slot tags; for a Block component,
the instance's replacement tag). Differences in signature fields alone never trip this gate — those
are governed by the orphan acknowledgement above. Reconcile the listed entries first, or re-send
with `acknowledgeOverwrittenEntries: true` on the responsible decision (or on the component content
PUT) to accept the loss. Consent is per component: acknowledging on one decision does not silence
the gate for another component in the same batch. Entries a decision explicitly names are never
refused — an `UpdateShared` entry becomes the head it pushes and an `AdoptHead` entry takes exactly
what it asked for — and neither is a bundle `import-apply`, whose take-theirs arm is already an
explicit choice to overwrite local content (§8).

### Error codes

Conflict (409) and not-found (404) responses carry the code in the problem-details `code`
extension field, next to any situational extension fields (`currentHeadVersionId`,
`expectedHash`, ...). Validation (400) responses embed the bracketed code in the message text,
keyed by the offending JSON path — and note the serializer camel-cases the first segment of
those keys (`sections[0].Blocks[2].SharedComponentId`, `sharedComponent.CorruptDefinition`), so
match keys case-insensitively.

| Code | Status | Meaning and recovery |
| ---- | ------ | -------------------- |
| `SharedComponent.UnknownComponent` | 404 | No component with that id. |
| `SharedComponent.UnknownVersion` | 404 | No such version of that component. |
| `SharedComponent.TargetNotFound` | 404 | The locator was well-formed but no node is there — your template read is stale; re-GET and re-resolve. |
| `SharedComponent.InvalidTargetLocator` | 400 | The locator is malformed or ambiguous (both or neither address forms), or it addresses a real node that is not a copy of the component the decision claims — fix the request; re-reading will not help. |
| `SharedComponent.PayloadAbsent` | 404 read / 409 write | The version is a hash-only lineage row without stored content. |
| `SharedComponent.NoVersions` | 409 | The component's chain has no versions at all. |
| `SharedComponent.AlreadyShared` | 409 | The promote target already carries a link (the existing component id is in the problem details). |
| `SharedComponent.ContentMismatch` | 409 | Attach or relink content differs from the head (`expectedHash`/`actualHash` in the details). |
| `SharedComponent.KindMismatch` | 400 | A block where a section is required, or vice versa. |
| `SharedComponent.InvalidLinkShape` | 400 | Malformed `SharedComponent*` fields in a template PUT (rule 9). |
| `SharedComponent.Retired` | 409 | The component is frozen — unretire it, or break the copy out. |
| `SharedComponent.HeadMoved` | 409 | Your `expectedHeadVersionId` is stale — re-read the component, re-decide, retry. |
| `SharedComponent.ConcurrentAppend` | 409 | A rival write to the same chain or a consumer template won the race — re-read and retry. |
| `SharedComponent.ConflictingDecisions` | 400 | Two decisions in one apply contradict each other. |
| `SharedComponent.OrphansNeedAcknowledgement` | 409 | See the orphan loop above. |
| `SharedComponent.OverwritesLocalEntries` | 409 | The fan-out would discard a consumer's local edits that no decision named — in inline or referenced entries of a linked section copy, or in an edited instance of a Block component; the details carry `workflowTypeId` and `overwrittenEntrySlots`. Signature-field differences alone do not trip this. Reconcile those entries first, or re-send with `acknowledgeOverwrittenEntries=true` to accept the loss. |
| `SharedComponent.UnknownCategory` | 404 | The category id names no vocabulary entry. |
| `SharedComponent.CategoryInUse` | 409 | Category delete refused while components still reference it. |
| `SharedComponent.CorruptDefinition` | 400 / 500 | 400: your submitted definition (or a bundle's own payload — e.g. a section ref pointing at a component the bundle does not carry) could not be read as required. 500: a *stored* definition could not be read — not your fault; report it. |
| `SharedComponent.AgentRuntimeDisabled` | 400 | The definition carries an agent-family block — `Agent` or `Verification` — but this deployment's agent runtime is off (§3's gate). Remove the block, or have the deployment enable `PlatformSettings:AgentRuntime`. |
| `SharedComponent.AgentExpansionScope` | 400 | The definition places a verification block inside a parameterized or looped section, or carries an agent reference that expansion cannot rewrite. It is invalid in every consumer, so refused at the definition mouth even though the runtime is enabled (with the runtime off, the `AgentRuntimeDisabled` refusal wins). Move the verifier to a plain section, or fix the reference the message names. |
| `SharedComponent.AgentBlockInvalid` | 400 | An agent-family block in the definition fails the block's own rules: blank skills, or a model (specific or tier) outside this deployment's allowed list (the message names the allowed ids), and for a verification block any of §3's member, criterion, pass-cap or fuse rules. Fix the block and re-send. |

The template-level operations in §2 and §7 carry their own codes in the same `code` extension:

| Code | Status | Meaning and recovery |
| ---- | ------ | -------------------- |
| `WorkflowTemplate.VersionConflict` | 409 | Your `baseVersionId` is stale or missing; the current head is in `currentHeadVersionId`. Re-GET, re-apply, retry. |
| `WorkflowTemplate.ConcurrentSave` | 409 | A rival save won the race on the same template. Same recovery as above. |
| `WorkflowType.AlreadyExists` | 409 | The workflow type id you asked to create is taken (duplicate, §7). Pick another id. |
| `WorkflowType.NotFound` | 404 | The workflow type does not exist, or has no content. |

## 7. Duplicating a workflow type (variants)

`POST /api/v1/workflow-templates/{typeId}/duplicate` copies an entire template into a brand-new
workflow type. Use it when a client needs its own flavour of an existing process rather than an
edit to the shared one — the alternative, rebuilding by hand, is what silently loses the prompt
tuning described below.

```json
{
  "newWorkflowTypeId": "CorporateAnalysisPart2EU",
  "name": "Corporate Analysis Part 2 (EU)",
  "description": "optional; defaults to the source's",
  "categoryId": "optional; defaults to the source's"
}
```

**What carries.** Every section, block, variable, document class and signature field
*definition*. Every Library link stays **live** — a duplicate of a linked copy is another linked
copy, so future component updates reach both. Every per-entry deviation carries too: hidden
slots stay hidden, broken-out entries stay broken out, ref entries keep pointing where they
pointed. Stable ids are **preserved**, which is what keeps a variant diffable and
regression-comparable against its parent block-by-block. The page overview prompts live in the
workflow itself (`Configuration.PageOverviews` and each document class's `PageOverviews`), so
they carry like every other definition.

**What does not carry** — the response's `skippedCarriers[]` is the authoritative list, and you
should surface it verbatim rather than paraphrasing it:

- **Organization access.** The duplicate is invisible to every organization until one is granted
  access. This is deliberate: a per-client variant must not auto-expose itself to everyone who
  could see the source.
- **Signature override values.** Every field falls back to its staff default until a client
  approves a new value through the normal pending/vote flow.
- **Counterparty type mappings**, and all **execution history** (baselines, regression runs, past
  executions) — those stay with the source.

**Lineage.** The new type records the id it was duplicated from. It points at the *immediate*
source, not the root, so a family is the transitive closure of those edges. The pointer is
allowed to dangle if the source is later deleted — "descended from something gone" is true and
useful — so never assume it resolves. It is written only by this endpoint: a value supplied to
the template PUT is ignored, so you cannot fabricate lineage.

**Responses.** **200** with `workflowTypeId`, `versionId` (the root of the duplicate's own
version chain — its `parentVersionId` is null, it is not a child of the source's history),
`contentHash` and `skippedCarriers[]`. **404** `WorkflowType.NotFound`
when the source does not exist or has no content. **409** `WorkflowType.AlreadyExists` when the
requested id is taken — pick another; the existing id is in the problem details. **400** when the
new id equals the source id or the category does not exist.

Note that the duplicate's first `contentHash` normally equals its source's head hash, because the
type id is excluded from the canonical contract. That is correct — the content really is identical
— but it means a hash never identifies which type you are looking at. The exception is a source
whose stored head predates a normalization the save applies: per-block signature dictionaries, for
instance, are hoisted to the workflow level on save, so a legacy-shaped source yields a copy whose
hash differs from that older head. No data is lost there — the fields move, they do not disappear —
so do not read a hash difference on such a source as a failed copy.

### Deleting a workflow type

`DELETE /api/v1/workflow-templates/{typeId}` deletes a type that only your organization uses and
answers **204**. The type, its versions, its runs and their reports, its validation cases, and the
documents uploaded for it with their stored files disappear from your organization at once; a
document that a run of another type also used stays. The weekly cleanup removes all of it for good,
the template and its version history included, once your organization's retention window has passed
(30 days unless your organization set another). Until then nothing can bring the type back through
the API, and its id stays taken. A **500** can leave the delete half done; send the same `DELETE`
again to finish it.

**404** `WorkflowType.NotFound` when the type does not exist or your organization cannot reach it.
**409** `WorkflowType.SharedWithOtherOrganizations` when another organization uses the type too, and
`WorkflowType.RunInProgress` while a run of it has not finished — wait for the run, then retry.
**400** `WorkflowType.Reserved` for a type the platform owns: its reserved types and the types a
module installs.

## 8. Transferring a workflow type between deployments

Duplicate (§7) copies a type *within* one deployment. Transfer moves a type *between* deployments
as a self-contained file — the **bundle** — that you export on one and import on another. The two
sides are peers: there is no fixed parent and child, so relatedness is worked out from content
history at import time, never assumed from which side exported.

`GET /api/v1/workflow-templates/{typeId}/export` returns the bundle:

```json
{
  "bundleFormatVersion": 1,
  "originWorkflowTypeId": "CorporateAnalysis",
  "workflowTypeName": "Corporate analysis",
  "workflowTypeDescription": "...or null...",
  "exportedAtUtc": "...", "exportedByUserId": "...",
  "templateJson": "...the template's stored definition, verbatim...",
  "templateHashTrail": ["<oldest>", "...", "<head>"],
  "components": [ { "componentId", "kind", "name", "description", "categoryId",
                    "headContentHash", "headDefinitionJson", "hashTrail": [ ... ] } ],
  "categories": [ { "id", "name", "description" } ]
}
```

The `hashTrail` on the template and each component — the content hashes of its past versions,
oldest first through the head — is what the target uses to work out relatedness. Ids are never
read as proof of identity: the same component edited on two deployments has different ids, and ids
are re-minted on every JSON edit. **What a bundle never carries** is disclosed in `skippedCarriers`
on both import responses (organization access, signature field values, counterparty maps,
execution history and regression baselines) — surface it verbatim.

### Import creates the target when absent

A deleted workflow type answers **404** `WorkflowType.NotFound` to both preview and apply; its id
stays taken until the weekly cleanup removes it, so import under a new id instead.

When the target workflow type does not exist — or exists with no template content — preview returns
a create-shaped result instead of an error: `template.relation` is `New`, `baseVersionId` is null,
and components are still correlated against this deployment's Library (a component can pre-exist
even when the type does not). Apply then lands the bundle's template as the first version and
creates/appends components exactly as an update would — all in the same transaction. Only when the
type row itself is absent does apply also create it (named from the bundle's `workflowTypeName`,
described from `workflowTypeDescription`) and seed the importing organization's access row; a
headless type row that already exists keeps its own name, description, and access untouched. Omit `baseVersionId` on a create apply; if the
target grew a template head between your preview and the apply, the apply is refused with a version
conflict — re-preview. A bundle predating `workflowTypeName` cannot create a type
(`SharedComponent.BundleTypeNameMissing`) — re-export it from the origin. A bundle whose
`bundleFormatVersion` is newer than this deployment understands is refused
(`SharedComponent.BundleFormatUnsupported`) — update the platform.

### Preview

`POST /api/v1/workflow-templates/{typeId}/import-preview` with the bundle as the body computes,
without writing anything, how the bundle relates to what you already hold. It returns a `template`
row, a `components[]` row per bundle component, the `baseVersionId` apply token, and
`skippedCarriers`. Each row carries a `relation`, the `correlationMethod` that produced it, the
correlated local head's `localHeadVersionId` (null when `New`), and — when it needs your
attention — a `conflictCode`:

| `relation` | Meaning |
| ---------- | ------- |
| `New` | No local counterpart — apply creates it. |
| `InSync` | Local and bundle heads already carry the same content. |
| `LocalAhead` | Your chain already contains the bundle head as an ancestor and has moved past it. |
| `BundleAhead` | The bundle head descends from your head — applying fast-forwards your copy. |
| `Diverged` | Neither head descends from the other; the arms resolve it. |
| `LocalRetired` | The correlated local component is retired — take-theirs is refused until you unretire it. |

`correlationMethod` is how the match was made — `Anchor` (a template node at the same stable id),
`HashTrail` (shared content history), `GuidEquality` (same id, same kind, but no shared history),
or `Create` (new here). A `Diverged` row whose histories share **nothing** carries
`conflictCode: "SharedComponent.DivergedLineage"` and **both** hash trails — that is the signal you
are about to reconcile two lineages that were never the same, which apply refuses unless you
acknowledge it. Because ref-bearing sections hash their referenced ids, a genuinely related section
can read as `Diverged` when only those ids differ (the `correlationMethod` discloses it) — the arms
still resolve it.

A Section-kind row can additionally carry `refWarnings[]` — advisory strings, one per entry
reference whose target would not survive the apply as-is (the target's local counterpart is
retired, has no stored head payload, or is not a Block component). Each warning embeds the code
the apply would fail with; preview reports, apply enforces.

### Apply

`POST /api/v1/workflow-templates/{typeId}/import-apply` applies the bundle with your decisions, all
in one transaction:

```json
{
  "bundle": { ...the same bundle you previewed... },
  "baseVersionId": "<the preview's baseVersionId>",
  "templateArm": "TakeTheirs",
  "acknowledgeUnrelatedHistory": false,
  "acknowledgeSignatureOrphans": false,
  "componentDecisions": [
    { "componentId": "<bundle component id>", "arm": "TakeTheirs",
      "expectedLocalHeadVersionId": "<the correlated local head from the preview>" } ]
}
```

- Each element's `arm` is **`TakeTheirs`** (adopt the bundle's version — append it as the new head
  and fan out to every local consumer, exactly like a Library push in §6) or **`KeepMine`** (discard
  the bundle's version for that element). `New` components are always created; the template arm
  either adopts the bundle template or keeps yours (components still land either way).
- `expectedLocalHeadVersionId` is **required for a correlated take-theirs** — echo the preview row's
  `localHeadVersionId`; a stale one is a 409 `SharedComponent.HeadMoved` naming the component.
- The response carries the template mutation (`versionId`, `contentHash`, `wasDeduped`),
  `componentOutcomes[]` (each `componentId`, `localComponentId`, and an `action` of `Created`,
  `Appended`, `Deduped`, or `Kept`, plus head tokens), and `skippedCarriers`. An idempotent
  re-import no-ops per element with `action: "Deduped"`.

Agent-family content — `Agent` and `Verification` blocks — transfers only onto a deployment whose
agent runtime is on: an apply whose landed template would carry agent-family blocks, or whose
bundle components would land an agent-family definition, even under `KeepMine` for the template,
is refused (`SharedComponent.AgentRuntimeDisabled` for definitions; the §3 gate error for the
template graph). Enable the runtime on the target, or strip the agent-family content at the
origin and re-export. The allowed-model list is also per-deployment:
any provider's id of a model the target allows is accepted, but a model the target does not
allow is refused, with the message naming the target's allowed ids. A block that names no model runs the
target's tier model, so it is refused when the target's allowed list lacks that model.

Previews go stale by design — apply re-computes everything inside its transaction and can 409:
`SharedComponent.HeadMoved` (a head moved since the preview), `SharedComponent.DivergedLineage` (a
zero-overlap take-theirs you did not `acknowledgeUnrelatedHistory`), `SharedComponent.Retired` (the
landed template would link a retired local component — unretire it first), or
`SharedComponent.OrphansNeedAcknowledgement` (the template change orphans signature overrides — the
same acknowledge loop as §6, via `acknowledgeSignatureOrphans`). On any 409, re-run import-preview
and retry with fresh tokens.

Every Section-kind component the apply lands (created or appended) also has its entry references
validated against the **post-apply merged set** — the components already on this deployment plus
what the bundle itself creates and appends, honoring your per-component decisions. A reference that
would not resolve fails the whole apply loud, before anything is written:
`SharedComponent.UnknownComponent` (target neither carried nor matched locally),
`SharedComponent.KindMismatch` (target is not a Block component — a tampered bundle, since export
refuses to produce one), `SharedComponent.Retired` (target's local counterpart is retired and your
decisions keep it), or `SharedComponent.PayloadAbsent` (the kept local head has no stored payload).
The preview's `refWarnings` flag these ahead of time. A bundle whose section ref points at a
component the bundle itself does not carry is rejected already at preview as
`SharedComponent.CorruptDefinition` ("the bundle is incomplete") — re-export from the origin.

## 9. Version history and diffs

Every successful save appends one immutable version to the type's chain. There is one chain per
workflow type, shared by every organization that uses the type. These four read-only routes answer
"what changed in this template, when, and by whom". They need the same editor role as `/content`
and, like it, are not filtered by organization.

### History

`GET /api/v1/workflow-templates/{typeId}/versions?top=30&cursor=<nextCursor>`

```json
{
  "workflowTypeId": "CorporateAnalysis",
  "versions": [
    {
      "versionId": "9f0c...",
      "contentHash": "5be1...",
      "parentVersionId": "41aa...",
      "createdAtUtc": "2026-09-20T10:15:02.123456Z",
      "createdByUserId": "b7e2...",
      "createdByName": "Ada Lovelace",
      "payloadStored": true,
      "payloadTrimmedAtUtc": null,
      "narrative": "Edited the prompt of Summary"
    }
  ],
  "totalCount": 42,
  "hasMore": true,
  "nextCursor": "638939...._41aa..."
}
```

- Newest first. `top` is 1 to 200 (default 30). When `hasMore` is true, pass `nextCursor` back
  verbatim as `cursor` for the next older page. The cursor is opaque; a value this endpoint did not
  issue, or a `top` outside the range, answers 400 with the message under `errors.cursor` or
  `errors.top`.
- `versions[0]` on the first page is normally the current head. `GET /content` is the authority
  for the head `versionId`: on two saves with an identical timestamp the two orders can differ.
- `narrative` is the plain-language summary generated and stored at save time. It survives
  retention. A type's first version reads "First saved version", or "Duplicated from <typeId>" when
  the type was created by duplicate (§7).
- `payloadStored: false` means retention cleared that version's template JSON
  (`payloadTrimmedAtUtc` says when). The row still lists, but its content and any comparison with
  it answer 404 `WorkflowVersion.PayloadTrimmed`, and so do its changes and the changes of the
  version after it, because both need its content. The one exception: a trimmed **first** version
  still answers `/changes` with `isFirstVersion: true`, since that needs no content.
- `parentVersionId` is the preceding version in the chain, null for the first. Read it as chain
  order only. Versions saved before the per-organization chains were merged into one had this
  pointer rewritten to chronological order, so it is never proof of which version an author opened
  before editing.
- `createdByName` falls back to the raw user id when no user matches. A service-user save shows the
  service user. The chain is shared across organizations, so an author can belong to another
  organization than yours.
- An unknown type answers 404 `WorkflowTemplate.NotFound`. A known type with no saves yet answers
  200 with an empty list.

### One version's content

`GET /api/v1/workflow-templates/{typeId}/versions/{versionId}/content` returns `workflowTypeId`,
`versionId`, `contentHash`, `parentVersionId`, `createdAtUtc`, `createdByUserId`, `narrative` and
`workflow`, the template JSON exactly as that save stored it (the same verbatim PascalCase shape
as `GET /content`).

### What a version changed

`GET /api/v1/workflow-templates/{typeId}/versions/{versionId}/changes` diffs the version against
the version before it in the chain (`parentVersionId`). `GET .../versions/compare?from={a}&to={b}`
returns the same shape for any two versions of the type, read from `from` to `to` exactly as given:
put the newer id in `from` to preview what restoring `to` would undo.

```json
{
  "workflowTypeId": "CorporateAnalysis",
  "fromVersionId": "41aa...",
  "toVersionId": "9f0c...",
  "isFirstVersion": false,
  "storedNarrative": "Edited the prompt of Summary",
  "narrativeParts": ["Edited the prompt of Summary"],
  "diff": {
    "isEmpty": false,
    "sections": [],
    "blocks": [
      {
        "replacementTag": "summary",
        "name": "Summary",
        "blockType": "AiQuery",
        "stableBlockId": "0d6f...",
        "sectionTitle": "Analysis",
        "changeKind": "Modified",
        "isLibraryLinked": false,
        "fieldDiffs": [
          {
            "fieldName": "Properties.UserPromptTemplate",
            "before": "Summarise the filing.",
            "after": "Summarise the filing in three bullet points."
          }
        ]
      }
    ],
    "variables": [],
    "documentClasses": [],
    "workflowFields": []
  }
}
```

- `changeKind` is `Added`, `Removed`, `Modified` or `TypeChanged` (blocks only; its single field
  diff is `Type`).
- `fieldDiffs[].fieldName` is a dotted path inside the element; `before` and `after` are the
  **whole** values, not excerpts, so a large prompt edit returns both whole prompts. Strings come
  back verbatim. Other scalars are rendered as text: booleans as `True` / `False`, and an ISO
  date-time string is re-rendered in a server format, so compare those by meaning, not by bytes,
  and read the exact stored value from the version content. An array whose elements all carry a
  non-empty, unique `Name` is keyed by name (`Properties.WorkerSkills[alpha].Content`); any other
  array is keyed by index, so an insert there shifts the later elements' paths.
- **Added and removed blocks and sections carry no `fieldDiffs`.** Read the block from the version
  content (`to` for an added block, `from` for a removed one).
- Blocks are matched by `ReplacementTag` across the whole template, variables by replacement tag,
  document classes by key, sections by the blocks they hold. Reordering alone is not a change. A
  block moved to another section with no other edit gets no block entry, though the sections it
  moved between can then read as `Modified`. A renamed `ReplacementTag` reads as one removed block
  plus one added block. `sectionTitle` is the `to` side's section (the `from` side's for a removed
  block).
- A `ReplacementTag` that repeats (rule 5 allows it across sections) yields one entry per block:
  same-tag blocks are told apart by `stableBlockId` when every one of them has one, else by their
  order of occurrence. Use `sectionTitle` and `stableBlockId` to tell the entries apart.
- If `diff.isEmpty` is true while the two versions' `contentHash` values differ, the change is in
  something the diff does not model; read both version contents.
- `stableBlockId` and `stableSectionId` are identity, never diffed. They are null on versions saved
  before stable ids existed.
- `workflowFields` holds changes to every template-level field outside `Sections`, `Variables`
  and `DocumentClasses`, such as the output title.
- `storedNarrative` is filled only by `/changes`. `narrativeParts` is computed from the diff: one
  sentence per changed variable, document class, section and block, plus summary sentences for
  workflow-level fields.
- The first version has nothing before it: `/changes` answers 200 with `isFirstVersion: true`,
  `fromVersionId: null` and `diff: null`.
- For versions saved before the chain merge, "the version before it" is the chronologically
  previous save of the type. The diff is a true content difference between those two saves, but do
  not report it as one author's edit of the other author's version.

### Errors

| Code | Status | Meaning |
| ---- | ------ | ------- |
| `WorkflowTemplate.NotFound` | 404 | `/versions` only: the workflow type does not exist. |
| `WorkflowVersion.NotFound` | 404 | No such version **on this type**. A version id that belongs to another type answers identically, so always address a version under the type it was listed from. |
| `WorkflowVersion.PayloadTrimmed` | 404 | The content of the version (for `/changes`, of it or of the version before it) was cleared by retention. Nothing can be diffed or restored from it; its `narrative` in the history list is what remains. The message names the version whose content is gone. |
| `WorkflowVersion.PayloadMissing` | 404 | The version row has no stored content and no retention stamp: damaged data. Treat it like a trimmed version and tell your operator. |

A bad `top` or `cursor`, or a compare with `from` or `to` missing, answers 400 as a validation
problem: the message is under `errors.<parameter>` and there is no `code`.

### Restoring a previous version

There is no restore endpoint; a restore is an ordinary save of old content on top of the current
head, so history is never rewritten.

1. `GET /{typeId}/content` and keep its `versionId`: the **current head**.
2. `GET /{typeId}/versions/{oldVersionId}/content` and take its `workflow`.
3. Optionally `GET /{typeId}/versions/compare?from={headVersionId}&to={oldVersionId}` and show your
   operator what the restore will change.
4. `PUT /{typeId}/content` with `{"workflow": <old workflow>, "baseVersionId": "<head versionId>"}`.
   The old version's own `versionId` is **not** a valid `baseVersionId` and answers 409.

The save validates the old content against today's rules, so a very old version can answer 400 or
return warnings; fix the named paths as for any save. If the old content equals the head the save
answers `wasDeduped: true`. The restored version's `contentHash` normally equals the old version's;
it differs when the save normalizes old-shaped content on the way in. If the restore changes linked Library copies, the reconcile loop (§6)
classifies them afterwards like any other local edit.
