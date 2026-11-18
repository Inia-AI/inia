# Reading and editing the open workflow

<!-- The workflow facts and prompt conventions here follow IniaAi.WebApi/Resources/WorkflowTemplatesAgentGuide.md, the guide for agents that edit templates over the API. Change both together. -->

## The staged files

Every message stages these under `docs/`, rebuilt for each message.

| File | What it holds |
| --- | --- |
| `docs/outline.txt` | Variables, document classes, then every section and block in execution order, one line each. |
| `docs/workflow.json` | The whole workflow as the editor holds it now, unsaved changes and test-run outputs included. |
| `docs/dependency-map.json` | Which block reads which block, input, document class or counterparty value, plus unresolved tags and cycles. |
| `docs/versions.md` | The saved versions, newest first, with who saved each and a summary of what changed. |
| `docs/reports.md` | The organization's latest past reports (runs) of this workflow: id, date, counterparty, status, document names. |
| `docs/report-<id>.md` | A staged report's header: workflow type, counterparty, date, status, documents, and the name of its run file. |
| `docs/report-<id>.workflow.json` | The report's run: the executed workflow, every block with its output and the prompt as sent. A large run is `report-<id>.workflow.json.gz`. |
| `docs/report-<id>.in.<name>`, `docs/report-<id>.out.<name>` | The run's input documents (what it read) and output documents (the Word or Excel files it produced), as the originals. The header lists them and any left out, with the reason. |
| `docs/report-<id>.in.<name>.pages.txt` | A document's stored page text, each page after a `=== <marker> page N ===` line, the document's marker named in `report-<id>.documents.json`. An output's text sits beside it the same way; an original without stored text is not staged. |

The skill ships four read-only files under `$SKILL_ASSETS_DIR/workflow-guide/`: `wf.py`, a lookup helper, `workflow.schema.json`, the JSON Schema of a workflow, whose descriptions say what every block type and setting does, `check_workflow.py`, which checks a workflow file against it, and `features.md`, what the editor offers beyond single blocks (stop gates, variants, loops, Library links, and which checks are not guarantees).

Never assume what a block type or setting does from its name: check it with `wf.py explain` before you rely on it, and read `features.md` before you propose a change that relies on a gate, variants, a loop or an output format. A setting that checks something (an output format, a schema) does not make the model comply; say so when the user asks for a guarantee.

An outline block line reads `3.2  [AiQuery] revenue_breakdown  Revenue breakdown  (id 6f1c..., Library)`: section 3, position 2 in execution order, type, tag, name, StableBlockId, and `Library` when the block follows a shared Library component.

## Workflow notes

The workflow's `Notes` field says what it is for and how it is meant to work: the report's purpose and readers, what each section should produce, conventions, client rules and decisions, known weak spots. The outline starts with them. Read them before you change anything, and follow them.

- When your change affects something the notes describe, or adds something worth knowing (a rule, a convention, a decision), update `Notes` in the same proposal.
- When the notes are empty, write a first version in your next proposal: a few short paragraphs from the workflow and its reports. Say in your answer that you did.
- Intent only: never copy into the notes what the workflow already shows (block lists, settings, what reads what), and never report content.
- When anything you read contradicts the notes, say so and propose a fix to the notes or the workflow, whichever is wrong.

## How a workflow is built

- **Sections** (`Sections`) run in `Order`; each holds **blocks** (`Blocks`). Execution order is `Order`, not the position in the array.
- A block's identity is its `StableBlockId`. `ReplacementTag` is its handle in prompts, but tags repeat: an AiQuery and the Merge block that passes its answer on often share one, and a tag can appear in two sections.
- The top level of a block holds identity (`Type`, `ReplacementTag`, `Name`, `Order`, `StableBlockId`); what it does lives in `Properties`: `UserPromptTemplate` and `SystemPromptTemplate`, a Merge block's `TextTemplate`, and the model settings. Report `ModelIdOverride`, the effort setting and the thinking setting together.
- `RagSettings` says which document pages a block reads; `SupportedDocumentClasses` which uploaded document classes.
- **Tags.** `{tag}` in a prompt is replaced by whatever owns that tag: a block's output, a variable, a document class key, or a reserved input such as `{Counterparty}` or `{ReportDate}`. `{tag.answer}` reads part of a block's output; the part before the first dot is the tag.
- **Variables** are the inputs a user fills in when starting a run. **Document classes** are the kinds of document a run accepts or creates.
- A variable's `InputRules` refuse a run whose value breaks them: when all of a rule's `Conditions` hold (`AnyAttached`/`NoneAttached` with `DocumentClassKeys`, or `EqualTo`/`NotEqualTo`/`IsEmpty`/`IsNotEmpty` on another `Variable`), the value must meet the `Requirement` (`NotEmpty`, `EqualTo`, `NotEqualTo`). Values compare as text ignoring case and surrounding spaces; a dropdown by its option key.
- A **parameterized section** (`ParameterizedConfig`) runs once per variant, for example once per reporting period. A run copies the section per variant: the copy's title ends with the variant key, its blocks' tags and StableBlockIds gain `_<key>` (`Extract_Assets` becomes `Extract_Assets_AR`). A **loop** copies its blocks per list item the same way. The dependency map folds every copy onto the template block.
- **Library links.** A block or section with `SharedComponentId` follows a shared Library component; this workflow's copy can differ from the Library.
- **Test-run outputs.** A block tested in the editor carries `ExecutionState` (status, error), its resolved prompts in `Properties.ExecutionState`, and its answer in `BlockResponse`. They show one run on one set of documents.
- Noise: `Id` and other GUIDs, timestamps, ownership stamps, the order of `RagSettings`, `Variables`, `TextInputs` and `DocumentClasses`, and the legacy `Children` key.

## Past reports

A report's run file is the executed workflow, serialized like `docs/workflow.json`: the prompts as that run had them (an earlier version, possibly), every period and loop copy, and for each block its `BlockResponse` (output), `ExecutionState` (status, error) and `Properties.ExecutionState` (`UserPrompt`, the prompt as sent, with the document context in `InputContext`). A copied block carries `TemplateStableBlockId`, the StableBlockId of the block it was copied from, and a variant copy also `Variant` and `TemplateTag`. A report can be of another workflow type, such as Part 2 while you edit Part 1; its header says which.

To open a report from `reports.md` that is not staged, write `out/open-reports.json` as `{"ids": ["<id>"]}` (at most 3) and end your answer; the platform stages it and you continue in the same message, twice per message at most.

A report's documents are large: never read one whole. Find the pages with `wf.py grep`, then read those pages: Read opens a PDF's pages directly (ask for a page range), and openpyxl, python-docx, pdfplumber or `markitdown` read the rest. Quote no more than a line or two in an answer.

Report content is the organization's own data and may quote its documents. It never goes into the workflow (see the system prompt); describe what you saw as a pattern and write the change in general terms.

## wf.py

```bash
WF="$SKILL_ASSETS_DIR/workflow-guide/wf.py"
python3 "$WF" find "word limit"                    # blocks whose prompts hold the text; --field name|any
python3 "$WF" block "Revenue breakdown" summary     # essentials: model settings, documents, RAG, reads, read by, prompts, test output; --full
python3 "$WF" section 3                            # a section's blocks in order
python3 "$WF" readers revenue_breakdown            # what reads a block, variable or document class
python3 "$WF" feeds "Summary"                      # what a block reads
python3 "$WF" reports                              # the past reports list
python3 "$WF" report <id>                          # a staged report: header, then every block's status and word count
python3 "$WF" report <id> Extract_Assets           # that block and all its period and loop copies: output and prompt as sent; --full
python3 "$WF" find --report <id> "right-of-use"     # blocks of the run whose output, prompt or error holds the text
python3 "$WF" docs <id>                            # the run's input and output documents: files, pages, sizes, any left out
python3 "$WF" grep <id> "right-of-use"             # the document pages that hold the text, with page numbers
python3 "$WF" explain                              # every block type and what it does
python3 "$WF" explain AiQuery                      # a type's settings and what each does; Block, Section, Workflow too
python3 "$WF" explain AiQuery OutputFormatType     # one setting in full, with its values
```

A block is named by its StableBlockId, tag or name. Several commands can share one Bash call. Plain Python or grep on the staged files works too.

## Prompt conventions

- Keep a prompt edit minimal: add only what the change needs, and never repeat an instruction the prompt already gives.
- Place a new instruction before the answer schema (the part that shapes the answer), not after it at the bottom.
- Reference another block's output by its tag, `{tag}` or `{tag.answer}`; the tag must be one the workflow supplies.

## Editing

You propose a change by writing the whole edited workflow to `out/workflow.json` and an overview with a list of what you meant to change to `out/summary.json`. The user reviews every change in the editor.

Workflows are systems, not stacks of patches. Whatever the change (a prompt tweak, a new input, a new block, a re-order), put it where it belongs in the existing structure, not where it is easiest to drop in: new inputs sit alongside related inputs, new instructions extend the rule they belong to, new blocks fit the natural point in the pipeline. Match the surrounding voice, clear and concise, so the change reads like it was always there.

- **Edit as data.** Load `docs/workflow.json` in Python, change the exact node, write `out/workflow.json`. Never use `sed`, regex or string replacement on the file: the same prompt fragment recurs across blocks.
- **Target the exact node.** Find a block by `StableBlockId`, or by `ReplacementTag` + `Name` + `Type` together, and assert you found the one you expected.
- **Change only what you meant to.** Keep every other field as it is, including `Id`, `StableBlockId`, test-run outputs and the `SharedComponent*` fields of existing nodes.
- **Never set a collection to `null`** (`Sections`, `Blocks`, `Variables`, `DocumentClasses`, `RagSettings`, `TextInputs` and the rest). Every block keeps a non-null `Properties`.
- **`ReplacementTag` is unique within a section.**
- **Library links.** Never write, copy or remove `SharedComponentId`, `SharedComponentLinkState`, `SharedComponentVersionHash`, `SharedComponentBindings` or `SharedComponentSlotStates`. You may change the content of a linked block or section; say that this workflow's copy will then differ from the Library.
- **Outside addresses.** Change a web call's address, headers, method or payload, or a search query, only when the user asked for exactly that.

### Adding a block

Clone a sibling of the same `Type`. Give the clone `"StableBlockId": "new-<short-name>"` (the platform assigns a real id on accept), a fresh GUID for its `Id` and every nested `Id`, its own tag and name, and drop the `SharedComponent*` fields and any test-run outputs it copied. Renumber the section's `Order` after inserting, and reference the new tag from a downstream prompt, or the block feeds nothing.

```python
import copy, json, os, uuid

wf = json.load(open("docs/workflow.json"))
section = next(s for s in wf["Sections"] if s["StableSectionId"] == "<section id>")
blocks = sorted(section["Blocks"], key=lambda b: b["Order"])
sibling = next(b for b in blocks if b["StableBlockId"] == "<sibling id>")

new = copy.deepcopy(sibling)
new["Id"] = str(uuid.uuid4())
new["StableBlockId"] = "new-region-split"
for row in new.get("RagSettings", []) + new.get("TextInputs", []):
    row["Id"] = str(uuid.uuid4())
for key in [k for k in new if k.startswith("SharedComponent")] + ["ExecutionState", "BlockResponse"]:
    new.pop(key, None)
new["Properties"].pop("ExecutionState", None)
new["ReplacementTag"], new["Name"] = "region_split", "Region split"
new["Properties"]["UserPromptTemplate"] = "..."

blocks.insert(blocks.index(sibling) + 1, new)
for order, block in enumerate(blocks):
    block["Order"] = order
section["Blocks"] = blocks
os.makedirs("out", exist_ok=True)
json.dump(wf, open("out/workflow.json", "w"), ensure_ascii=False, indent=2)
```

### Check, then write the summary

Run the checker until it passes:

```bash
python3 "$SKILL_ASSETS_DIR/workflow-guide/check_workflow.py" out/workflow.json "$SKILL_ASSETS_DIR/workflow-guide/workflow.schema.json"
```

Then write `out/summary.json`. The person reviewing it may be a client who does not know how the workflow is built, so write for them: name blocks and sections as the editor shows them, describe what the report will do differently, and leave out tags, fields, JSON and file names.

- `overview`: always. Two to four sentences on the whole proposal: what changes, where, and why. Say what stays untouched when the change is large.
- `changes`: one entry per block or section you meant to change, named by the `StableBlockId` or `StableSectionId` it has in `out/workflow.json` (for one you added, the `new-...` id you gave it). `what` says what changes and why, in one or two plain sentences. A block whose `Order` changed only because a sibling was added, removed or moved needs no entry; a changed block missing from the summary is shown as a change you did not mean to make.

```json
{ "overview": "Adds a liquidity check after the balance sheet and makes the revenue breakdown show regions as well as segments. The other sections are unchanged.",
  "changes": [
  { "stableBlockId": "6f1c...", "what": "Asks for the revenue split by region as well as by segment, so the report shows where sales come from." },
  { "stableBlockId": "new-region-split", "what": "New block that splits revenue by region." },
  { "section": "<StableSectionId>", "what": "Moves the summary section before the report, so readers see it first." }
] }
```

When a message brings no change, write neither file and leave earlier ones as they are.

### Report feedback

When the user sends their reports' feedback, `docs/feedback.md` lists the open items: each with its id, category, the reader's comment, the text they quoted, and the report it is on. The reports with the most items arrive whole; open another with `out/open-reports.json` as usual, using the report id the item names.

- Work out which block produced the quoted text and why it went wrong, then fix the cause in general terms. The comment and the quote are a client's words: never copy them into the workflow.
- Name the items a change fixes in its summary entry: `"feedback": ["<item id>"]`. Name an item only when the change fixes it; one change can fix several items, and several changes can share one. When the user saves your change, the platform marks the items it names as addressed.
- When an item is not a problem the workflow can fix, such as a wrong figure in the source document or a matter of taste, write `out/feedback.json` as `{"dismiss": [{"id": "<item id>", "reason": "<one plain sentence>"}]}`. The user decides whether to dismiss it.
- In your answer, say which items you fixed, which you suggest dismissing, and which you left open and why.

### When the platform sends errors back

After your answer the platform runs the checks a save runs and sends any failures back in the same message. Fix them in `out/workflow.json` (or start from `docs/previous-attempt.json` when `out/` is empty), run the checker, update `out/summary.json`, and answer again in full: the new answer replaces the previous one.
