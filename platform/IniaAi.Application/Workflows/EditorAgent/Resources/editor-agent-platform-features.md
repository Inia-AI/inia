# What the workflow editor offers beyond single blocks

Read this before you propose a change that relies on one of these features. For one block type or
setting, `wf.py explain` is quicker.

## Checks are not guarantees

- **Output format** (`OutputFormatType` on an AI Query block) checks the answer's shape (JSON, one
  tag, several tags, JSON inside a tag), retries once when it does not match, then fails the block.
  It never makes the model answer in that shape. To make the shape reliable, state it in the prompt
  (with an example), then add the output format as the safety net; `OutputFormatJsonSchema` tightens
  the JSON check.
- **SwitchCase** maps a value to a fixed output. It does not branch the workflow: every later block
  still runs.

## Skipping a section or a block

- **Stop term `AF11`.** A section's text input (`TextInputTemplate`, "Section text input" in the
  editor) or a block's text input is checked after its placeholders are filled. When the value holds
  `AF11` as a whole word, the section or block is skipped. The usual pattern: a block answers `AF11`
  when the section does not apply, and the section's text input reads that block's tag.
- A section is skipped when every block in it that reads documents has none of the documents it
  needs.

## Running a section more than once

- **Variants** (a parameterized section). `ParameterizedConfig` is a JSON string on the section:

  ```json
  {"Variants": [
    {"Key": "AR", "Label": "Annual report",
     "Params": {"_PERIOD_DESC": "the financial year"},
     "SectionStopGate": "{check_annual.stop}",
     "ClassMap": {"FinancialStatements": ["AnnualReport"]}}
  ]}
  ```

  The section runs once per variant. Each copy's tags and StableBlockIds gain `_<Key>`
  (`Extract_Assets` becomes `Extract_Assets_AR`), `Params` are words replaced in its prompts before
  it runs, `SectionStopGate` is that copy's stop gate, and `ClassMap` swaps the document classes its
  blocks read. A later block reads a copy by its suffixed tag.
- **Loops.** The blocks from a LoopStart to its LoopEnd run once per item of a list; each pass copies
  them the same way.

## Shared and protected parts

- **Library components.** A block or section with `SharedComponentId` follows a component shared
  across workflows. Editing it makes this workflow's copy differ from the Library; never write or
  copy the `SharedComponent*` fields.
- **Signature fields** are workflow-level values an organization can override without editing the
  workflow; a prompt uses one as `{Name}`.
- **Variables** are the inputs a user fills in when starting a run; a prompt uses one as `{Tag}`.

## What reaches outside the platform

Web API Call, Intelligent Search, Exchange Rate and Peer Financials call outside services. A Web API
Call to an internal address is refused. The user confirms every change to these on its own.
A Web API Call header takes an API key as a stored-key placeholder, `{StoredKey.Name}`, as in
`Bearer {StoredKey.PerplexityApiKey}`; never write a key into a header value.

## Agent blocks

The Agent block runs an agent with skills on the agent runtime, only where the deployment enables
it. A Verification group checks its Agent members' output and sends failing ones back; Verification,
Json2Xlsx and Peer Financials run only in the paid build.

Every Agent and Verification run's system prompt starts with an `<environment>` section (the
sandbox, its tools, `docs/`, `out/`) and a `<task_setup>` section (the skills as `skills:<name>`,
the skill file paths, and whether an output schema is enforced); the authored system prompt follows,
unchanged, inside `<instructions>`. The runtime states the staged documents in the first user turn.
Never write those into a prompt or a skill; the prompt says only what the task needs.
