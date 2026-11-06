---
type: reference
status: current
description: "Complete configuration reference for every workflow block type: the settings each editor shows with their defaults, what each block outputs, how it fails, the Agent block, the blocks only the paid build runs, and the placeholder system with substituted-value sanitization. Read when you configure a block, a block's output or error surprises you, or you write a {tag} that reads another block."
---

# Block Configuration Guide

**On this page:** Overview · Common Block Settings · Core Blocks · AI-Powered Blocks · Data Blocks · Financial Blocks · Control Blocks · Utility Blocks · Data Transformation Blocks · Document Processing · Placeholder System · Best Practices · Common Patterns · Related Documentation

Complete reference for configuring workflow blocks in the IniaAi Platform.

## Overview

Blocks are the fundamental execution units in workflows. Each block type has specific configuration options and capabilities. This guide provides detailed configuration instructions for all available block types.

> [!TIP]
> **A few terms used throughout this page**
>
> - **[Counterparty](../../reference/glossary.md)** — the company or entity a report is about (a fund, an issuer, a borrower). Every screen calls it a company. A block can write a result back to a field on the counterparty's profile.
> - **[RAG](../../reference/glossary.md)** (retrieval-augmented generation) — the platform first finds the most relevant pages of your uploaded documents, then feeds only those pages to the AI. It is how a block answers questions grounded in a long document without sending the whole thing.
> - **[Workflow type vs. workflow vs. execution](../../reference/glossary.md)** — a *workflow type* is the reusable template you author here; a *workflow* (or *execution*) is one run of that template against specific documents and inputs.

### Blocks that only the paid build runs

The Json2Xlsx, Peer Financials and Verification
blocks run only in the paid build. Their code is a closed module that a build from the open
source does not include (`ModuleBlockTypes`). There the block types stay, so a workflow keeps
such a block with its stored settings:

- The editor shows the block with its name, label and colour, and a panel that says the platform
  does not include such blocks, so the block cannot run. Saving keeps the block and its settings unchanged. The block-type list
  offers none of the three types, except on a block that already has one.
- A run fails the block with the message "The platform does not include `<label>` blocks, so
  this block did not run. Remove the block if the workflow does not need it. The full edition
  includes `<label>` blocks.", where `<label>` is the block type's name in the editor. The run then goes on as it does after any failed block.
- The members of a Verification group run as plain Agent blocks, and their output reaches the
  report.

## Common Block Settings

All blocks share these core configuration options:

### Basic Properties

| Property | Description | Required |
|----------|-------------|----------|
| **Name** | Display name (editor only) | Yes |
| **Replacement Tag** | Reference identifier for block output | Yes |
| **Include in Output** | Add block result to final workflow output | No |
| **Paragraph Sub-title** | Heading for this output in final report | No |
| **Save Output for Company** (`ShouldUpdateCounterpartyWithResult`) | Save the block's output back to the counterparty, which the editor calls the company: either its Last Financial Analysis Result, or a freeform **Property** merged under a key you name (later read with `{Counterparty.key}`; keys match case-insensitively). | No |
| **Is Critical** (`IsCritical`) | Whether this block's failure aborts the whole execution instead of being absorbed. Unset follows the block type's default (see [critical blocks abort the run](../../architecture/workflow-engine/parallel-execution.md#critical-blocks-abort-the-run)); an explicit value overrides it either way. In the editor: **Advanced settings** › **On failure** › **If this block fails**, with **Follow the block type**, **Stop the run** and **Continue the run**; following the block type leaves the property unset. The editor's **More actions** menu also has **Set every block to stop the run**. The setting belongs to each workflow, not to a Library component: a linked copy keeps its own value. | No |
| **Runs alone** (`RunsAlone`) | Runs the block in a scheduler layer of its own, so nothing runs beside it ([barriers](../../architecture/workflow-engine/parallel-execution.md#barriers-run-alone-layers)). | No |


### Advanced AI Model Settings

The AI Query block's model and input settings, with the editor's labels (`AiQueryProperties`):

| Setting | Description | Default |
|---------|-------------|---------|
| **Maximum Response Tokens** | Maximum output tokens (≈ 4 chars each) | 8000 |
| **Use Advanced Model** | Run the deployment's advanced model instead of its basic model | On |
| **Use Thinking Mode** | Enable reasoning before the answer | Off |
| **Thinking Budget** | Tokens allocated for reasoning; the editor keeps it below Maximum Response Tokens | 1024 |
| **Effort** | How eagerly adaptive models spend tokens (Low–Max) | Model default (High; Medium on Opus 5.5 and Haiku 5.5) |
| **Override Model Selection** (**Provider**, **Model**) | Run one named model instead of the basic or advanced one | Off |
| **Use Caching** | Send the document context and the cached prompt section as a cached prefix, when the model connector supports caching | Off |
| **Cached prompt section** | Text sent ahead of the prompt as the cacheable part | Empty |
| **Use Page Overviews Instead of Full Text** | Send each retrieved page's overview, not its full text | Off |
| **Include Page Images** | Send the retrieved pages' images with their text | Off |
| **Process Pages in Batches** | Split the retrieved pages into batches and ask the model once per batch, with **Chunking Mode**, **Pages per Batch** (20), **Max Characters** (50,000), **Preserve Order** (on) and an optional **Stop Tag** that ends the batches early once an answer holds that tag with a value other than NA | Off |

**Model Selection:**
- **Use Advanced Model** off runs the deployment's basic model, on runs its advanced model; which models those are is deployment configuration ([how the choice resolves](../../architecture/ai-integration/providers.md#block-model-choice-resolves-per-stack))

**Thinking Mode:**
- Allocates tokens for internal reasoning
- Thinking Budget must be < Maximum Response Tokens; the editor lowers it to one below when you lower the maximum
- Improves accuracy on complex tasks
- Adaptive-generation models (Haiku 5.5, Sonnet 5, Sonnet 5.5, Opus 4.8, Opus 5, Opus 5.5) decide thinking depth themselves: the budget does not apply, and when Thinking Mode is off the platform disables thinking explicitly on every model that allows it
- Opus 5, Opus 5.5, Sonnet 5.5 and Haiku 5.5 limit how Thinking Mode and Effort combine, and the editor shows a note under the control when a limit applies — see [Adaptive-generation request shape](../../architecture/ai-integration/providers.md#adaptive-generation-request-shape)

**Effort:**
- Sent only to adaptive-generation models (Haiku 5.5, Sonnet 5, Sonnet 5.5, Opus 4.8, Opus 5, Opus 5.5); other models ignore it
- Controls how eagerly the model spends tokens across thinking, tool use, and output
- Levels: Low, Medium, High, Extra High, Max; the dropdown marks the selected model's default
- Leave unset to keep the model default — High on every model except Opus 5.5 and Haiku 5.5, whose default is Medium; works whether or not Thinking Mode is on
- With Thinking Mode on, the Thinking Budget does not apply to these models. If the model spends the whole Max Tokens on thinking and writes no answer, the request is sent once more at Low effort

**Caching:**
- Useful when multiple blocks or batches send the same content
- The provider's own rules decide what is cached and for how long

**Page Images:**
- Sends visual elements alongside text
- Essential for charts, graphs, visual tables
- Increases token usage and cost

## Core Blocks

### AI Query

Sends prompts to AI models for text generation and analysis.

**Purpose**: Direct AI interaction with custom prompts

**Configuration:**

| Property | Description |
|----------|-------------|
| **System Prompt** | AI behavior and role definition |
| **User Prompt** | Actual query or instruction |
| **Model** | The deployment's basic model, or its advanced model with **Use Advanced Model**. **Override Model Selection** picks a model by name, such as Claude Opus 5.5, and each deployment runs it under its own id. A model the deployment cannot call fails the block ([how the choice resolves](../../architecture/ai-integration/providers.md#block-model-choice-resolves-per-stack)) |
| **Document context** | Supplied automatically from the block's RAG settings — no placeholder needed |
| **Get Citations From Model** | Asks the model to name the page behind each figure. The editor ticks it when the block starts to read documents through a filter rule: when you add the first filter rule while the document classes are **Any** or a class, or when you change the document classes from none selected or **None** to **Any** or a class while the block has a filter rule. A switch of the block type to AI Query ticks it too when the block has a filter rule and reads **Any** or a class. An edit to the prompts or the text inputs ticks it when the block starts to read pages through **Retrieve Data from Documents** blocks: at least one such input reads page text, not overviews, and such inputs are no fewer than its **AI Query** and **Agent** inputs. An edit to a block that already read pages this way keeps your choice. A new block, which reads **Any** document but has no filter rule yet, starts with it off. A block with none selected reads every document at run time, but the editor treats it as not picked yet. Untick it to turn citations off; a later change between **Any** and classes keeps your choice. Opening or saving a workflow does not change it. Only a block whose output can reach the report acts on it: a block that reaches none asks the model for no citations ([Which Blocks Are Citation Sources](../../architecture/citations/index.md#which-blocks-are-citation-sources)) |

**Placeholders:**
- `{BlockName}`: Output from previous blocks
- `{variable_name}`: Workflow variables

Retrieved document pages are **not** referenced by a placeholder — a block with RAG settings receives them automatically as input context. The reserved `{InputContext}` token is **not** substituted (see the placeholder reference below).

**Example Use Cases:**
- Financial analysis from documents
- Content summarization
- Data extraction
- Question answering

**Tips:**
- Clear, specific prompts yield better results
- Use system prompt for role/formatting instructions
- Test prompts with different inputs

#### Output Format — turning a free-text answer into named values

By default an AI Query block produces one block of free text. That is fine when the answer goes straight into the report. But when a **later block needs to pick one specific value back out** — a rating, a yes/no, a number, a structured record — free text is hard to reference reliably. The **Output Format** setting solves this: your prompt asks the model to wrap its answer in **named tags** (or to return JSON), the platform **checks the answer has the expected shape** (and asks the model once more, with the same prompt, if it doesn't), and each named piece then becomes referenceable downstream as `{BlockName.tagName}`.

You set it in the editor on the AI Query block, in the **Output format validation** panel: pick a **Format**, then fill in the tag name(s) or schema it asks for.

| Format (editor label) | What it means | When to use it |
|-----------------------|---------------|----------------|
| **None (no validation)** | Raw free-text answer, no checking. | The answer goes straight into the report as prose. |
| **Single Tag** | The answer must contain **one** named tag wrapping the value, e.g. `<rating>…</rating>`. | You want the model to think freely, then deliver one clean value a later block will read. |
| **Multi Tag** | The answer must contain **several** named tags, in any order, e.g. `<reasoning>…</reasoning> <answer>…</answer>`. | You want the model to surface multiple named pieces in one call (a verdict *and* its justification). |
| **JSON** | The **entire** answer must be valid JSON. | The output will be consumed as structured data (e.g. fed to a chart). |
| **JSON in Tag** | Valid JSON **inside** a single named tag, so the model can also write reasoning outside the tag. | You want structured data *and* free-text reasoning in the same response. |

You still write the instruction to use the tags in your **prompt** — for example "Put the one-word rating inside `<rating></rating>`." The Output Format setting is what then *enforces* it: if the model forgets the tag or returns malformed JSON, the platform retries automatically, and shows you the rejected attempt in the editor if it ultimately can't comply.

**How the named values flow downstream.** Once a Single Tag / Multi Tag / JSON in Tag block validates, each tag's content is extractable by name. So an AI Query block named `Score` that emits `<rating>` and `<confidence>` lets a later block reference `{Score.rating}` and `{Score.confidence}` directly — for example in a Calculate Number formula or another prompt — instead of re-parsing free text. For **JSON** and **JSON in Tag** output you reach inside the structure by dot-path with `{Score.fieldName}` or `{Score.Json.fieldName}`; both read the validated JSON, also when the model wraps the JSON in a code fence. (See the [Placeholder System](#placeholder-system) below.)

> [!TIP]
> **Full guide**
>
> The **[AI Query Output Format](ai-query-output-format.md)** guide covers the Simple Schema Builder, the field types and constraints, the lenient validator and its error messages, the Advanced (raw JSON Schema) mode, retry behaviour, and the limitations — everything beyond this short intro.

#### A refused block

When the model refuses an AI Query block, the block **fails** and has no output. This covers a provider refusal (`stop_reason: "refusal"`, with or without partial output) and a refusal the model writes as its whole answer ([how the platform tells them apart](../../architecture/ai-integration/providers.md#model-refusals)). The block's error reads "The model refused to answer this block (refusal category: reasoning_extraction), so the block has no output.", and its log row carries the category in `RefusalCategory`. The tokens the refused call spent are recorded. Nothing is retried on another model. A block that splits its pages into chunks fails as a whole when the model refuses any one chunk, and the other chunks' answers are not kept, so the block never reports a partial answer as complete. A block that depends on it receives the no-data placeholder text, never the refusal. The report page shows a notice that lists each refused block and page with its reason, and the execution logs mark each one "Refused by the model". A prompt that asks for the model's reasoning written out in the answer, for example in `<reasoning>` tags, is the usual cause of a `reasoning_extraction` refusal on Sonnet 5.5 and Opus 5.5.

### Calculate Number

The editor calls it **Calculate Formula**. Performs mathematical calculations on numeric values and dates.

**Purpose**: Mathematical operations and formulas

**Configuration:**

| Property | Description |
|----------|-------------|
| **Formula To Calculate** | The expression, with `{BlockName}` / `{variable}` placeholders for its numbers and dates |

**Supported Operations** (the formula is evaluated by NCalc, with built-in function names matched case-insensitively):
- Arithmetic: `+`, `-`, `*`, `/`, `%`, comparisons and `if(condition, a, b)`
- NCalc's math functions, such as `Sqrt()`, `Pow()`, `Abs()`, `Round()`, `Min()`, `Max()`
- Date functions the platform adds: `addDays(date, n)`, `addMonths(date, n)`, `addYears(date, n)`, `diffDays(a, b)`, `today()`, `year(d)`, `month(d)`, `day(d)`, `startOfMonth(d)`, `endOfMonth(d)`. A bare `yyyy-MM-dd` date that a placeholder leaves in the formula is quoted first, so it reaches these functions as a date rather than as a subtraction. A date result is written as `yyyy-MM-dd`.
- References: `{BlockName}` for numeric outputs

An empty formula, or one holding the no-data text because an input had no data, writes no output and does not fail. A formula NCalc cannot evaluate fails the block with `Unable to process the formula '<formula>': <reason>`.

**Example:**
```
Formula: ({Revenue} - {Costs}) / {Revenue} * 100
Result: Profit margin percentage
```

**Use Cases:**
- Financial ratios
- Metric calculations
- Data aggregation

## AI-Powered Blocks

### Intelligent Search (ExaSearch)

AI-powered web search with advanced filtering and subpage crawling.

**Purpose**: High-quality web research with AI understanding

**Configuration** (editor labels; the full property list is on [Intelligent Search architecture](../../architecture/workflow-engine/exa-search-block.md#intelligentsearchproperties)):

Every text setting takes `{tags}`, filled in when the block runs: the search term, search type, result category, the published dates, the crawl strategy, the summary prompt and schema, and each entry in Include Domains, Exclude Domains and Subpage Keywords. A domain such as `{ParentURL.answer}` becomes the URL the earlier block found. A domain that is still not a domain after filling, such as "No data available", is left out, and the search then covers the whole web.

**Search Settings:**

| Property | Description | Default |
|----------|-------------|---------|
| **Search Term Template** | Search term (supports placeholders) | Required. With no search term, for example when the block it reads has no data, the block is skipped with the reason "No search term available". A search that errors fails the block. |
| **Search Type** | auto, fast, keyword, neural | auto |
| **Number of Results** | Number of results | 10 |
| **Result Category** | Exa result category, e.g. company, news, research paper | None |
| **Published After** / **Published Before** | ISO 8601 date filters | None |

**Content Retrieval:**

| Property | Description | Default |
|----------|-------------|---------|
| **Page Text** | Retrieve each page's text; with it on, **Include HTML Tags** and **Max Characters** appear | Unset (Exa's default) |
| **Highlights** | Get highlighted snippets | Unset |
| **AI Summary** | AI-generated summaries | Unset |
| **Summary Prompt** / **Summary Output Schema (JSON)** | Custom summary instruction and structured-summary schema | None |
| Document class selector | The document class the results are saved under as a document for later blocks ([Documents from blocks](../../features/docs-from-blocks.md)) | None |

**Live crawl, domains and subpages:**

| Property | Description |
|----------|-------------|
| **Crawl Strategy**, **Timeout (seconds)** | Exa live-crawl strategy (never, fallback, always, preferred) and its timeout |
| **Include Domains (comma-separated)** | Allowed domains; a full URL is reduced to its host |
| **Exclude Domains (comma-separated)** | Blocked domains |
| **Subpages Per Result (1-10)** | Related pages to crawl per result |
| **Subpage Keywords (comma-separated)** | Keywords that pick the subpages |

**Auto-Addition Logic:**
- When Include Domains is set and Subpages is not, the block crawls 3 subpages per result
- There are no default subpage keywords
- A result whose text would span more than `MaxPagesPerResult` pages (default 100; template JSON only, no editor control) is dropped

**Output Structure:**
```xml
<searchResults>
  [Main pages and their subpages]
</searchResults>
<searchSummaries>
  [AI summaries if enabled]
</searchSummaries>
<searchHighlights>
  [Key highlights if enabled]
</searchHighlights>
```

**Use Cases:**
- Competitor research
- Market analysis
- Fact-checking
- Source gathering

**Best Practices:**
- Use domain filtering for focused results
- Specify subpage targets for relevant crawling
- Enable summaries for content analysis
- Balance subpage count with performance

### Retrieve Data from Documents (RAG)

Document retrieval using RAG strategies, without an AI answer: the block outputs the retrieved pages.

**Purpose**: Extract relevant document sections for later blocks

**Configuration:**

| Property | Description |
|----------|-------------|
| **Get Overviews** | Output each retrieved page's overview instead of its full text |
| RAG settings (**Select The Filter Type To Use For Documents' Pages**) | The retrieval strategy and its inputs, below; an AI Query, Build Topics or Agent-family block carries the same settings |
| **Document Classes** | Which documents the block reads |

**Available RAG Strategies** (editor labels; a new RAG setting starts on **Top N Pages With 2 (two) Neighbors Per Filter Expression**):

| Strategy | Description | Use Case |
|----------|-------------|----------|
| **Whole Document/All Pages** | Complete document(s) | Small documents, full context needed |
| **Top N Pages** | Most relevant pages via similarity | Large documents, focused extraction |
| **Top N Pages With 2 (two) Neighbors Per Filter Expression** | Top pages per filter expression plus the page before and after each | Contextual understanding |
| **Top Page** | Single most relevant page | Quick lookup, specific answer |
| **Top Page With N Following Pages** | Top page + next N pages | Sequential content |
| **Take N Pages Starting From An Index** | Pages from a specific position | Known document structure |
| **Keyword Based Selection** | Pages containing a keyword; optionally searches page overviews and includes neighboring pages | Exact term matching |
| **Exact numbers of pages** | Specific page numbers, with an optional number of following pages | Manual page selection |
| **RAG Agent** | Iterative, multi-round page selection with reasoning (**Query Template**, **Iteration Limit**, **Context Limit**) | Complex retrieval where a single similarity pass is insufficient |
| **Use Table Of Contents** | Sections rebuilt from a table of contents you supply | Structured documents |
| **Use Auto-Detected Table Of Contents** | Sections from a table of contents the platform detects | Structured documents without an explicit TOC |

**Similarity strategies' inputs** (each a template, with an "After Execution" twin showing the resolved value):

| Property | Description |
|----------|-------------|
| **Start Index Template** | First page to consider |
| **End Index Template** | Last page to consider |
| **Number Of Pages To Use Template** | Pages to return per filter expression |
| **Smart Filter Expression Template** | Semantic search phrases, one filter expression each |

How each strategy selects pages: [RAG architecture](../../architecture/rag/index.md).

**Use Cases:**
- Document Q&A
- Content extraction
- Multi-document synthesis
- Topical analysis

### Agent

The editor calls it **AI Agent**. Runs a skill-driven agent on the external agent runtime, via `AgentBlockStrategy`. The
type is **environment-gated**: the editor's block selector offers it, and saves accept
it, only where `PlatformSettings:AgentRuntime:Enabled` is on (see
[Application Configuration](../../operations/application-configuration.md#agentruntime)). The
setting defaults to off, so a deployment that has not turned it on shows no AI Agent
entry in the type selector at all — the first thing to check when the block is missing
from an editor that otherwise looks current. The gate holds at
**every** write that can land agent content on a deployment — template saves from both
surfaces, shared-component fan-out, materialize, reconcile apply, duplicate, transfer
import, and the Library's definition mouths (create, definition replace, version
restore), which refuse with `SharedComponent.AgentRuntimeDisabled`. An Agent block may sit
inside a parameterized or looped section (see the loop and parameterized rules below).


The gate also holds at **submission**: a workflow that contains an agent block is refused
when the runtime is off — `WorkflowExecution.AgentRuntimeDisabled`, from the shared
orchestrator behind the API submit paths — **before any execution row exists**, so a
template authored while the flag was on cannot burn its upstream OCR and RAG spend only to
die at the Agent block. The editor's Generate Report button does not run through the
orchestrator; it applies the same guard client-side before it enqueues (see
[which enqueue paths carry which gate](../../architecture/workflow-engine/block-strategy-pattern.md#strategy-catalog)).
The strategy's own execution-time failure remains as the backstop for anything that slips
past submission.

**Two authoring rules are easy to get wrong, and both are enforced for you:**

**1. Agent prompts use double-brace tags — and autocomplete inserts them that way.**

The system prompt and user prompt render under the strict double-brace agent-template grammar: `{{tag}}` resolves to a value, while single-brace text (JSON literals, prose braces) stays literal. Inside an agent block the prompt editor's tag autocomplete therefore inserts `{{tag}}`; in every other block it keeps inserting the engine's single-brace `{tag}`. A single-brace tag typed by hand into an agent prompt is not an error — it is simply literal text the run will receive verbatim.

**2. The output JSON schema is validated at save against the dialect the runtime actually enforces.**

The optional output schema (structured output) is checked at save — and again by the client before any upload — as **JSON Schema draft-07**, exactly as the agent runtime's SDK compiles it (Ajv in strict mode, formats not validated). A schema outside that dialect is refused with the offending JSON-pointer path in the message rather than burning a paid run. In particular:

- **Unknown keywords are rejected**, including every 2020-12-only keyword (`prefixItems`, `dependentRequired`, `unevaluatedProperties`, …) and misspellings (`propertys`, `minmum`).
- Keyword shapes follow the draft-07 meta-schema (`required` is an array of unique strings, `enum` is a non-empty unique array, numeric limits are numbers, …).
- `$schema`, if declared, must be draft-07; `$ref` must be a resolvable local `#/...` pointer — the SDK loads no external schemas.
- The schema is sent verbatim: `{{tags}}` are **not** applied inside it.

**What every run is told automatically.** Every agent run's system prompt
starts with two sections the author never writes, in this order. They are delimited with XML
tags rather than markdown headings, so they never clash with the `#` headings of an authored
prompt:

1. `<environment>`, written by the agent runtime: the sandbox and its tools, the working
   directory, `docs/` with its `manifest.json`, `out/`, how to invoke a skill, and the read-only
   `$SKILL_ASSETS_DIR`. The runtime also serves this text at `GET /v1/environment`.
2. `<task_setup>`, written by the platform from the start request (`AgentRunFacts`). It opens
   with "Added automatically by the system." and has one bullet per item that applies: the
   skills as `skills:<name>`, comma separated (names only; the SDK already lists each skill with
   its description); the skill files as `$SKILL_ASSETS_DIR/<skill>/<file>`, comma separated, or
   one per sub-bullet when there are more than three; and "The final answer is checked against
   an enforced output schema." when the block has one. With no bullets there is no
   `<task_setup>` at all.

The authored system prompt follows, unchanged, inside `<instructions>`, which is left out when
the system prompt is blank. The staged documents are not listed: the runtime states every
staged document with its document class in the first user turn. Do not restate any of this in a
prompt or a skill: say only what the task needs, such as when to use each skill. The editor
shows both sections in a collapsed, read-only **Added automatically** panel under the prompts.
The panel shows the runtime's text, or **Environment text not available** when the runtime
predates the endpoint or agents are disabled, and this block's **Task setup**, rendered by the
same code as the run.


For a block with two skills, one skill file and an output schema, the platform's part of the
system prompt reads:

```text
<task_setup>
Added automatically by the system.
- Skills: skills:balance-sheet, skills:verify-bs
- Skill files: $SKILL_ASSETS_DIR/balance-sheet/check.py
- The final answer is checked against an enforced output schema.
</task_setup>

<instructions>
...the block's authored System prompt, unchanged...
</instructions>
```

**Configuration Fields:**
- **Worker Skills**: the skills bundled into the agent run. Each carries a **name**, a **description**, and its **content**, and at least one skill is required at save time.
  - The **name** is the skill's identity for invocation, not a file name: lower-case words joined by single hyphens, no extension, at most 64 characters (`income-statement`). A name outside that shape is refused at save time, and the message names the identity it would have been — the platform never silently rewrites a name, so the name you type is the one the model invokes. Two names that differ only by shape (`Income Statement` and `income-statement`) are the same skill and are refused as a collision.
  - The **description** is **required**, and it is the field the whole mechanism runs on: the runtime writes it into the skill's frontmatter, and the SDK reads it at session start to decide the skill exists and when to reach for it. A skill sent without one never registers and the run fails outright with `skill_register_failed`, so the platform refuses the save and refuses the start request rather than spend a run on a certain failure. Say what the skill does *and* when to use it. Capped at 1400 characters.
  - Descriptions and content are both templatable under the double-brace agent-template grammar; names are not.
  - **Skill files**: a skill can ship up to 16 extra files, such as a script or a lookup table, from the **Skill files** panel of its card. Each file is a name and its pasted text. The runtime stages them read-only at `$SKILL_ASSETS_DIR/<skill name>/<file name>`, and the skill's content tells the model to read them there. A file name must match `^[A-Za-z0-9._ -]{1,200}$`, be unique within its skill, and not be `manifest.json`, `.` or `..`, and every file needs content. A save that breaks one of these rules is refused, and the start request is checked the same way, so no run is spent on a file the runtime would refuse. By default file text is sent as written: it is not rendered under the agent-template grammar, so <code>{{tag}}</code> inside a file stays literal, which keeps a Python f-string or a Jinja template safe. To fill tags in one file, tick **Fill <code>{{tags}}</code> in this file** on it (off by default; the content field's label then changes from **File content (no <code>{{tags}}</code>)** to **File content (<code>{{tags}}</code> filled at run time)**). A ticked file is treated exactly like skill content: its tags are filled when the block runs, the block waits for every block the file reads, an unknown tag in it is a save warning like one in skill content, and a parameterized or looped section rewrites its tags per variant. Every <code>{{...}}</code> in a ticked file that looks like a tag is filled, so leave a file unticked when it holds literal double braces. The flag is stored as `FillTags` on the file and is left out of the saved workflow while it is off, so an existing workflow's content is unchanged. File bytes count toward the same size budget as the run's documents.
- **Model tier** (`ShouldUseAdvancedModel`): which model the block runs when it names no specific model, as on an AI Query block, but with the agent path's own pair. On, the deployment's advanced agent model (`PlatformSettings:AgentRuntime:AdvancedModel`, default `claude-opus-5-5`); off, its standard agent model (`PlatformSettings:AgentRuntime:BasicModel`, default `claude-sonnet-5-5`). An organization can replace either with `AgentAdvancedModel` / `AgentBasicModel` in `PlatformSettings:ModelRouting:TenantModelOverrides`. It defaults to on, as AI Query's does, both on a new block and on a stored block saved before the field existed. The editor labels the toggle with the model it resolves to for the current organization (**Advanced model (Claude Opus 5.5)**) and states under it which model the block runs. The tier model resolves against `AllowedModels` like a specific model, and a save or submit is refused when the list has no form of it ("names no model of its own, so it runs the advanced agent model …"). Configuration: [AgentRuntime](../../operations/application-configuration.md#agentruntime)
- **Model** (**Specific model** in the editor): optional, and overrides the tier when set; the editor then disables the tier toggle and says the specific model overrides it. Clearing it hands the choice back to the tier. A new block starts with none. The value is a provider-neutral model id (`claude-sonnet-5-5`), restricted at save time and at submit time to the models in `PlatformSettings:AgentRuntime:AllowedModels`. Any provider's id of an allowed model is accepted (`us.anthropic.claude-sonnet-5-5` on a stack that lists `claude-sonnet-5-5`, or the reverse) and saved as the neutral id. When the block runs, the id resolves to the allowlist's form: a Bedrock inference-profile id on an AWS stack, a Foundry deployment name on an Azure stack, an Anthropic API model id on a direct-API platform paired with the runtime's `anthropic` mode. A workflow therefore moves between stacks without editing the block. The runtime keeps its own allowlist and answers a disallowed id with a `400` naming the permitted set, so a stale editor dropdown cannot smuggle a model past it. What the list holds per deployment mode: [The agent path in Azure mode](../../architecture/ai-integration/azure-foundry.md#the-agent-path-in-azure-mode)
- **Effort**: optional reasoning-effort override (`low`, `medium`, `high`, `xhigh`, `max`), a clearable select in the editor. Unset omits effort from the start request entirely, so the runtime/SDK default applies — there is no platform default. A set value outside the vocabulary is refused at save time, and the runtime answers an unknown level with a `400` before the run owns any state
- **User prompt**: what the agent is asked to do. It should **not** point at skills by name or path — they are registered with the SDK and chosen by their descriptions, so naming them in the prompt is redundant and goes stale. Blank falls back to a default that states the task and nothing else
- **System prompt**: optional. It follows the two sections every run is told automatically (above), so it holds only what the task needs
- **Output JSON schema**: optional JSON Schema the final answer must conform to. Sent **verbatim** — never templated, because a schema is structure rather than prose
- **Output files** (`OutputFiles`): optional list of JSON files the agent writes under `out/`, one path per line in the editor (`out/rows.json`). Empty by default, which keeps the block output as the agent's answer. See [Output files](#agent-output-files) below
- **Run limits** — **max run time** (minutes), **max spend per run** (USD), **max model calls**, and **max output kept** (MiB): four optional numeric fields, no switches. You author them in human units; the start request carries the runtime's own milliseconds and bytes. A fuse you fill in is declared to the runtime and may only **tighten** it — never widen it, and never clamped, so a misconfiguration is loud rather than silently different. A fuse you leave **blank** is not declared at all: the field is omitted from the start request and the runtime's own deployed default governs the run. There is no platform-side default for any fuse, so the platform never declares a number the author did not choose, and blocks saved before fuses existed simply declare nothing. Values at or below zero are refused at save time. **Every fuse binds one run, not the block**: a block that retries on infrastructure trouble starts a fresh run entitled to the declared budget again, so worst-case block spend is the budget times `MaxRunPasses` (see below). Like model and effort these are engine configuration: never templated, and read straight off the block
  - **Fuses are bounded by the runtime's own advertised limits.** The platform fetches the runtime's deployed default and ceiling per fuse (`GET /v1/limits`) and caches them (`AgentRunLimitsCache`, refreshed in the background), so the ceilings are never hardcoded platform-side — they move with runtime deployments. The editor carries the deployed default in each field's placeholder (unit-converted to the field's own unit), so an empty field states what will govern the run, and caps the field at the ceiling. At save time the guard refuses a fuse above its ceiling, **naming the ceiling in the message**, so the template cannot be saved into a state where every later run dies at start with a `400`
  - **A runtime outage never blocks a save.** When the limits are unknown — the runtime unreachable at save, or the cache stale — the ceiling check is skipped and the save **succeeds with a warning** saying the fuse values went unchecked. The runtime's own `400` at run start remains the enforcement either way, refusing an over-ceiling fuse **before any spend**
  - **Max output kept is a share of deployment capacity, not per-run disk.** The runtime charges the declared value against **one retained-output budget shared by every agent run on the deployment**, reserved at admission — so a large value here quietly reduces how many runs can be admitted at once, for every workflow and every customer on that deployment. Declare only what the deliverables actually need
- **Document Classes**: which uploaded documents reach the agent (below)

<a id="agent-output-files"></a>
**Output files.** An Agent block with **Output files** set passes script-made data on exactly as
the script wrote it, instead of having the model retype it in its answer. Retyping 1,250 bank rows is
slow, costs output tokens, and can change values; a file the agent's script wrote under `out/` is
passed on byte for byte. With the list set, the block output is one JSON object:

```json
{"answer": {"status": "ok"}, "files": {"rows": [{"amount": 1250.10}]}}
```

- `answer` is the agent's final answer: embedded as JSON when it is valid JSON, otherwise as a JSON
  string. The **Output JSON schema** still checks only the answer. An answer wrapped in a code fence
  is not valid JSON, so it arrives as a string and `{tag.answer.status}` finds nothing; set an
  output schema when later blocks address fields of the answer.
- `files` holds one key per listed file, named by the file's stem (`out/checks/rows.json` is
  `rows`), with that file's content written verbatim.
- Later blocks read both halves with ordinary placeholders: `{tag.answer.status}`,
  `{tag.files.rows}`, `{tag.files.rows[0].amount}`. `{tag}` alone is the whole object, verbatim. A
  sub-path such as `{tag.files.rows}` re-serializes the object it selects, as every
  [object sub-path](#object-sub-paths) does: values, number text and letters survive as written.
- The platform appends one line to the agent's task naming the files to write
  (`AgentRunOutputFiles.TaskInstruction`), so the prompt and skills do not have to repeat it.
  The line goes in the task (the user prompt), not in `<task_setup>`.
- Each path is `out/`, optional folders, then a name ending in `.json`, using letters, digits, `_`
  and `-`. Two paths with the same stem are refused at save (`AgentViolationKind.OutputFilesInvalid`).
- The files come from the run that produced the answer, never from an earlier pass or round: the
  platform reads them from that run's collected `out/` rows (`AgentRunOutputFiles.SettledOutFilesOf`).
- The block fails, naming the file, when a listed file is missing (the agent did not write it, or
  the platform's 32 MiB collection cap left it out), when a file is not valid JSON (nested deeper
  than 62 levels counts as not valid), or when the composed output is over 8 MiB. It also fails when
  the platform could not collect the run's files at all. The run's spend is recorded first, and the
  run's ledger row is marked failed with subtype `output_files_unusable`. The runtime's own activity record still shows the run as succeeded, because the model call itself succeeded.
- Turning the setting on for an existing block moves its answer under `answer`: later blocks'
  tags that read the block must change to match.
- `{tag}` alone substitutes the whole object, files included, into a later prompt; read the field
  a later block needs instead of the whole output.




**Scoping documents by class.** The block's document-class selection decides what the
agent can read:

| Selection | What the agent receives |
|-----------|-------------------------|
| Empty, or **Any** | Every uploaded document |
| **None** | No documents at all — deliberate, for tasks that work only from prompts and skills |
| One or more classes | Only documents whose current class is one of them |

The filter runs **when the block executes**, not when the document was uploaded
(`Block.GetClassScopedDocuments`), so editing the class list after uploading re-filters
what ships and a document whose class no longer matches is dropped, with a log line
counting the exclusions. A block scoped to concrete classes with **nothing matching
fails before the run starts**, naming the classes in the error, rather than spending a
billed run on an empty workspace.

Documents that pass the filter are staged into the run's workspace as real files under
`docs/`, beside a `manifest.json` giving each file's name, media type, document class,
size and SHA-256; skills are staged separately. The staged name is the document's display
name with its real file extension reattached (`Document.FileName` — the display name alone
is extensionless by design), so skills can recognize a PDF as a PDF. A document name that
breaks the runtime's staged-name rules (letters, digits, dot, underscore, space, hyphen,
up to 200 characters) is rewritten to fit — keeping the extension when trimmed for length —
and the substitution is logged, rather than failing the run.

A block that declared a deadline polls until that **deadline plus a patience margin**
(`RunDeadlineMarginSeconds`, 2 minutes by default), so it sees the runtime's own verdict
instead of racing it. A block that declared none is governed by the runtime's deadline, which
the platform does not know, so it polls for `MaxPollDeadlineMinutes` (30 minutes by default) —
the consumer's own patience, with no margin added. A run that pauses to ask a question fails the
block — the Agent block is single-pass and cannot answer.

**Protecting what the workflow already spent.** An Agent block usually runs last, after OCR,
RAG and every upstream block has already cost real money, so the block absorbs the failures
that say nothing about the work itself. It waits out a capacity refusal instead of failing on
it, buys a fresh run when a pass fails on infrastructure trouble (a drained sidecar, an
exhausted uid pool, an internal error, or a runtime that no longer knows the run it was
given), tolerates a few failed polls rather than voiding a live run, and cancels any run it
walks away from so nothing keeps billing unwatched. The knobs are deployment configuration —
see [AgentRuntime settings](../../operations/application-configuration.md#agentruntime); the mechanism
lives in the shared executor, described in
[Agent Runtime Integration](../../architecture/services/infrastructure/agent-runtime-integration.md).

**What absorbing those failures costs, stated plainly.** Both prices below are accepted
deliberately — an agent block is the last thing a workflow buys, and failing it outright throws
away everything upstream already paid for — but neither is bounded by the fuses an author sets.

- **Spend multiplies with retries.** Every fuse binds a *run*, not the block. A retry is a fresh
  run entitled to the whole declared budget again, so a block's worst-case spend is the declared
  budget times `MaxRunPasses` — with a $2.50 budget and the shipped limit of 3 passes, $7.50.
- **Block wall time is deliberately unbounded by the fuses.** Each pass may spend its admission
  patience queuing (`AdmissionPatienceSeconds`, 10 minutes) and then its full poll deadline
  running (the declared deadline plus `RunDeadlineMarginSeconds`, or `MaxPollDeadlineMinutes`
  when nothing was declared). At the shipped defaults that is 3 × (10 + 30) minutes ≈ **2 hours**
  before an Agent block gives up, plus a ten-minute wait (`UpstreamErrorRetryDelaySeconds`) when a
  run failed because the model service broke and the block buys its one outage retry. Deployments
  that need a tighter ceiling lower `MaxRunPasses`, `AdmissionPatienceSeconds` or that wait; a run
  deadline alone will not do it.

**What a run actually cost, and what a failure leaves behind.** A run is billed from its tokens at
list price: one usage row per model the run called (`UsageType.LlmAgentRun`), priced request by
request from that model's pricing row, beside a spend row (component `AgentRunSpend`) whose units
are the number of passes and whose cost is only what no pricing row covered, such as a run whose
trace was withheld ([What an agent run reports to usage](../../architecture/services/infrastructure/agent-runtime-integration.md#what-an-agent-run-reports-to-usage)). When every pass fails, the block's failure
names the run id, which pass it was, the runtime's failure subtype and what was spent, and
the same ledger is persisted on the executed block's execution state. The ledger row is
written the moment a run starts — flushed durable before the first poll, carrying an
`Unacknowledged` marker until a complete is accepted — so a process death mid-run still
leaves a row that names the burning run, and a failed Agent block is a starting point
rather than a dead end.

**An Agent block's failure fails the whole execution.** It is the one type that is
critical by default, because it carries the run's deliverable and a report missing it is
worth nothing while having cost everything upstream. The run ends `Failed` with the
reason on the execution rather than `Draft` with a hole in it; opt out by setting the
block's **If this block fails** setting to **Continue the run** (under **Advanced settings**,
**On failure**). Mechanism:
[critical blocks abort the run](../../architecture/workflow-engine/parallel-execution.md#critical-blocks-abort-the-run).

**Trace evidence.** When the run settles — succeeded, failed, or cancelled — the platform
collects the run's trace pack off the runtime before acknowledging it, and attaches the
files to the executed block (`AgentRunTraceFiles`,
[UserData reference](../../reference/database/user-data-database.md#agentruntracefiles)). In the
editor, an **Agent run trace** panel appears under the block's results for any run with
collected traces: a summary header (model, turns, tokens with the cached share, cost, stop
reason), a latency chip (wall clock / API time / tool time), the run environment (tool
count, the skills the run enabled as the runtime's envelope records them, permission mode,
Claude Code version; a trace older than envelope v6 shows every skill the CLI found at start,
its built-in ones included), any permission denials, a
per-model usage table when a run spans models, a chronological tool-call/stage timeline,
one further timeline per spawned sub-agent (labeled `subagent: <name>`, parsed from that
agent's own `trace/sub/agent-<name>.jsonl` transcript), and each raw NDJSON file
expandable and downloadable. In the timeline a Bash step reads as the agent's own
plain-English description of what it was doing when the agent wrote one, and as the raw
command otherwise; the command itself always stays in the raw file below. A run that
spawned no sub-agent shows the main timeline only. Test executions show the panel from
the in-memory run — nothing needs to be persisted first. A failed collection never fails
the block; the run is simply left unacknowledged so the pack stays recoverable on the
runtime for its retention window.

Collected traces also ride the workflow JSON exports — the editor download and both
executed-workflow downloads (API and app) — and uploading such a file restores the panel
from the embedded rows. Inside the export each `trace/` file is inlined as a readable JSON
array of its NDJSON records, not a base64 blob, so a downloaded snapshot can be read
offline; `out/` artifacts stay base64 because they may be binary and their sha256 is
re-verified when a run resumes. Uploading rebuilds the newline-delimited bytes and
re-hashes them, so the panel parses an uploaded export exactly as a freshly collected
pack, and an export made before this shaping (base64 traces) still imports. A workflow
without traces exports unchanged.

**Agent-template grammar** — skill content follows a stricter grammar than the
[placeholder system](#placeholder-system), split by brace count:

- **Single-brace stays inert.** Single-brace tokens, JSON literals, and
  <code>${{ ... }}</code> CI syntax inside a skill survive authoring and dispatch
  untouched, on both the template and the executed copy. The engine never substitutes
  into skill content; the agent-runtime integration renders skills itself when execution
  lands.
- **Double-brace <code>{{tags}}</code> are engine references.** A tag must be
  identifier-shaped (letters/digits/underscore start, then dots, dashes, brackets — no
  spaces) and not `$`-prefixed, so Jinja/Helm-style spaced braces and GitHub-Actions
  <code>${{ ... }}</code> literals stay inert. Each engine tag produces a
  scheduling edge from its producer (`AgentTemplateTags` feeding
  `Block.GetAllPlaceholders`, the same surface the graph builder and dispatch read), and
  arrives resolved in the block's dispatch inputs. A tag no producer supplies arrives as
  the configured no-data sentinel (`PlatformSettings:IniaAi:NoDataForPlaceHolderText`).
- **Reserved namespaces.** Tags under `Validator` and `Verifier` (e.g.
  <code>{{Validator.decision}}</code>) are supplied by the runtime's own strategy
  loop, never by the engine — the scan excludes them, so they create no edges, no
  dispatch inputs, and no save-time warnings.

**Save-time checks** (every graph write — the editor and content-API saves via the
command validator, and all shared-component/transfer writes via `AgentBlockGuard` on
`WorkflowSavePipeline`):

- A skill, task prompt or system prompt override referencing a <code>{{tag}}</code>
  that matches no block tag, workflow variable, document class, signature field, reserved
  input, or text input of the block itself never stops a save. The save goes ahead and
  warns, naming the tag and the block: the editor shows the warning as a notice, and the
  content API, a Library insert and a copy to another workflow return it in `warnings[]`.
  The tag reads as the no-data sentinel at run time until its producer exists.
- An **Agent block may sit inside a looped or parameterized section.** Expansion rewrites
  its skills (content and description), task prompt and system prompt override per
  iteration, as it rewrites every other field: a sibling reference
  <code>{{facts}}</code> becomes <code>{{facts_N}}</code> in variant `N` and
  <code>{{Item_facts_0}}</code> in loop iteration 0, a parameter
  <code>{{_REGION}}</code> becomes the variant's value, and the loop item
  <code>{{Item}}</code> becomes <code>{{Item_0}}</code>. Only double-brace tags
  are rewritten; single-brace text and skill assets stay as written. Each copy is its own
  block, so it buys its own run (the run id carries the copy's block id), records its own
  cost row and trace rows, and has its own stable id (suffixed with its variant or
  iteration). The section's parameters and the loop's list title count as
  known tags for the unknown-tag check. An Agent block after the loop that reads a
  loop-body producer's whole output (<code>{{echo}}</code>) receives every
  iteration's output, one per line, as other blocks do. One loop may start at most
  `PlatformSettings:AgentRuntime:MaxLoopAgentCopies` agent runs (default 20); a longer
  list fails the run before any of them starts. An Agent block is critical by default, so
  one failed copy fails the run; make a looped agent non-critical when the other copies
  should still finish. The editor refuses a single-block run of an agent inside a loop
  body, because the loop expands only when its section runs.
- **Warned, not refused:** a tag the expansion cannot fill. That is an Agent block **outside**
  a parameterized section that reads a producer inside it by its template tag (after expansion
  only `tag_<variant key>` exists, so read a variant tag or move the agent into the section),
  and an Agent block after a loop
  that reads a field or an index of a loop-body producer (<code>{{echo.answer}}</code>),
  which the fan-in never rewrites. Like an unknown tag, it reads as no data at run time, so
  the save goes ahead and its warning says how to fix the reference.
- Materializing an agent-block component whose skills reference a tag the **target**
  template lacks lands it, and the response's `warnings[]` names the tag. A definition
  **push** is not refused for a consumer-side unknown tag either: it lands and the
  consumer's save entry carries the warning.
- A **signature-field rename** whose old name is referenced by a skill, task prompt or
  system prompt override is refused with `Signatures.RenameTouchesAgentSkill` — renames
  never rewrite agent templates, so the reference must be updated by hand first. All three
  rules read one enumerator of agent-template tags (`AgentTemplateGrammar`), so a prompt
  reference is caught wherever a skill reference is.


**Citations — the output can name where each figure came from.**

An agent run's structured output may carry a top-level `citations` property, and the
platform resolves each entry against the very documents it staged for the run:

```json
"citations": {
  "totalAssets": {
    "document": "Annual Report 2025.pdf",
    "page": 267,
    "as_printed": "Total assets    36    4,027,553,656    3,318,824,587"
  }
}
```

- **`as_printed` is the anchor for every format** — the cited row copied as printed. The
  platform searches the staged document's pages for it (tolerating the reformattings
  models legitimately apply: dropped thousands separators, a bracket negative rendered as
  a minus, added currency symbols — while a changed digit or a flipped sign still
  misses), and the page that carries the quote becomes the citation's page.
- **`page` is the 1-based page of the PDF file** — never the number printed on the paper,
  which the platform's page model cannot resolve. It is optional, only meaningful for
  PDFs, and acts as a preference when the quote appears on several pages. For
  spreadsheets and Word documents there is no locator at all: the file plus the quoted
  text is the whole citation.
- **`document`** is the staged file name (as listed in the run's inputs). It may be
  omitted when the block stages a single document.
- The property may be a map of field name to entry (or list of entries) or a flat list;
  an entry may also be a bare quoted string. Malformed entries are skipped — model
  output never fails the block.

Resolved citations ride the existing report citation surface — clickable facts, page
preview, numerical grounding against the page's real text — exactly like AI Query
citations. A quote that cannot be located anywhere in the named document (a hallucinated
line, an unknown file name, a scanned PDF with no text layer) is kept and rendered as
**unverified**: the file name with an "Unverified" badge, no page link — visible as
unchecked, never silently dropped and never shown as confirmed. Asking for citations is
the skill author's job: instruct the model to record each figure's row as printed (and
the file page for PDFs) under `citations` in the output schema.

**Referencing the run's output from a later block.** A later block references an agent
block like any other block, with the engine's single-brace tag: `{tag}` substitutes the
whole structured output as text, and a dotted tail walks into the JSON, so
`{tag.fields.totalAssets}` substitutes that one value. There is no `answer` member to
reach for: on JSON output the tail is a JSON path, and `{tag.answer}` resolves to nothing
unless the schema declares an `answer` property or the block lists
[output files](#agent-output-files), which wrap the output as `{"answer": …, "files": …}`. Facts a later block draws from the
substituted output keep the agent's resolved citations: the page links and the unverified
state follow the figures into that block's report section, with no document context of
its own needed.


## Data Blocks

### Web API Call

HTTP requests to external APIs.

**Purpose**: Integrate external services and data sources

**Configuration:**

| Property | Description | Default |
|----------|-------------|---------|
| **URL Template** | API endpoint (supports placeholders) | Required |
| **HTTP Method (GET, POST)** | GET or POST | GET |
| **Content-Type Header** | Request content type | application/json |
| **Accept Header** | Response content type | application/json |
| **Headers** | One row per header, each a **Header Key** and a **Header Value** (supports placeholders) | None |
| **Request Payload Template (JSON)** | JSON request body (POST) | None |
| **Delay After Request (seconds, for async APIs)** | Seconds to wait after the call (ignored when polling is enabled) | None |

**Stored keys:** put an API key in a [stored key](../../features/user-management/stored-keys.md), not in a typed header value. A header value names it with `{StoredKey.Name}`, for example `Bearer {StoredKey.PerplexityApiKey}`; **Insert stored key** under the value adds the placeholder. The request carries the value, and the workflow, its run records and a download keep the placeholder. `{Organization.ParallelApiKey}`, `{Organization.PerplexityApiKey}` and `{Organization.FmpApiKey}` name the stored keys of those names. A typed value in an `Authorization`, `X-API-Key` or `Api-Key` header shows a warning in the editor. A name the running organization has no key for fails the block before it sends anything.

**Substituted-value safety:** placeholder values substituted into the URL are percent-encoded and values substituted into the payload are JSON-escaped automatically (see [Substituted-value sanitization](#substituted-value-sanitization)); a POST payload with a JSON content type is also parse-validated after substitution, so a malformed payload fails the block locally with the parse error instead of a remote 400. A POST with a non-JSON content type whose payload template embeds placeholders fails the block with a descriptive error — substituted values are JSON-escaped, which would corrupt a non-JSON body; make the whole payload a single placeholder instead (single placeholders substitute verbatim). Header values containing line breaks fail the block with a descriptive error; a header the HTTP stack rejects for other reasons is skipped with a logged warning.

**Internal addresses:** the block refuses to connect to an internal address: loopback, private, carrier-grade NAT (`100.64.0.0/10`), link-local (which holds the cloud metadata address `169.254.169.254`), unique-local, unspecified, reserved, benchmarking, local-use NAT64 or deprecated IPv4-compatible, in IPv4, IPv6 and IPv4-mapped form, and the Azure host platform address `168.63.129.16`. The check runs on the address each host name resolves to, for the first request, every redirect hop, every poll and the completion URL. A direct connection opens only to the address that was checked, so a redirect, a DNS answer or a numeric address form such as `http://2852039166/` cannot get around it. Behind an egress proxy (`HTTP_PROXY`) the proxy resolves the name, so the check covers what the name resolves to from where the platform runs ([configuration reference](../../operations/application-configuration.md#outboundaccess)). This holds whatever fills the URL: a placeholder, an earlier block's output or a run-time variable. A refused call fails the block at once, with no retry, and the message names the host and the rule, for example `Refused to connect: 169.254.169.254 is a link-local address. A workflow reaches an internal service only through an approved exception: see "Calling an Internal Service from a Workflow" in the platform documentation.` For a host name the message names the rule but not the address the name resolves to (`Refused to connect: reports.internal resolves to a private address.`), so a workflow author cannot map internal names; the server log keeps the address. A workflow that must call an internal service gets an exception only through [Calling an Internal Service from a Workflow](../../operations/internal-service-calls.md), which ends in an entry in the deployment's `PlatformSettings:OutboundAccess:AllowedInternalHosts`. An entry opens the service to every workflow on that deployment.

> [!WARNING]
> **Internal services need an approved exception**
>
> This block cannot call a service inside the deployment's own network. To call one, follow [Calling an Internal Service from a Workflow](../../operations/internal-service-calls.md). Do not add an entry to the setting without it.

**Cookies:** a cookie that a response sets is sent on the later WebApiCall requests of the same run, including its polls, its completion URL and its redirect hops, to the hosts and paths the cookie names. Another run, of the same workflow or of another organization, never sees it.

**Poll-Until-Condition Mode:**

For long-running ("async") APIs, the block can poll a status endpoint natively instead of chaining separate blocks with fixed delays. Polling is opt-in — it activates only when a status JSON path is configured.

| Property | Description | Default |
|----------|-------------|---------|
| **Poll Until JSON Path** | Dot-path into the response (e.g. `run.status`) whose value is the completion signal. Setting this enables polling. | null (polling off) |
| **Poll Until Success Values** | Comma-separated status values that mean "done"; required when polling. | - |
| **Poll Until Failure Values** | Comma-separated status values that mean "failed"; the block fails fast on any of these. | - |
| **Poll Interval Seconds** | Wait between polls (clamped to a 5s minimum). | 15 |
| **Poll Timeout Seconds** | Overall polling deadline (clamped to 1 hour max). | 600 |
| **Poll Completion URL** | Optional second URL fetched once polling succeeds; its body becomes the block output (e.g. a `/result` endpoint after the status poll). | null |

When polling, each poll is matched case-insensitively against the success and failure value sets; transient errors (5xx, timeouts, network failures) are tolerated until the deadline unless five polls in a row fail (`… polling: 5 consecutive request errors; last error = …`), while `400`/`401`/`403` and a refused internal address fail fast. A deadline that passes with no success or failure value fails the block.

**Retries and rate limits:**

Every request the block sends (the single call, each poll, and the completion fetch) is retried on transient failure, and each wait is logged at `Warning` with the block, the status, the attempt number and the delay.

| Response | What the block does |
|----------|---------------------|
| `429 Too Many Requests` | Waits the server's `Retry-After` (delta-seconds or HTTP-date, capped at 120 s) when one is sent; otherwise a jittered exponential backoff (0.5 s up to 30 s per wait). Up to 8 retries within a 120 s total backoff budget; a `Retry-After` that does not fit in the remaining budget fails the block at once rather than waiting and then failing. |
| `5xx`, `408`, `425`, request timeout, network error | The fixed 1 / 2 / 4 / 8 / 16 s schedule (5 retries), unchanged from before. |
| `400`, `401`, `403`, `404` and every other `4xx` | Fails on the first response; these will not heal by retrying. |
| An internal address the deployment does not allow | Fails before any request is sent; see **Internal addresses** above. |

When retries run out the block fails with a message naming the final status, the attempts made and the total time waited (for example `WebApiCall to api.perplexity.ai failed with HTTP 429 Too Many Requests after 9 attempts and 116s of backoff: the 8 rate-limit retries are used up`), so a rate limit is distinguishable from a bad payload in the executed workflow.

Parallel blocks that target the same host share a process-wide **concurrency gate**, so the platform stops manufacturing its own 429s: at most `PlatformSettings:WebApiCall:MaxConcurrentRequestsPerHost` requests (default 8) are in flight per host, with per-host overrides under `PlatformSettings:WebApiCall:MaxConcurrentRequestsByHost` (`api.perplexity.ai` is seeded at 3). The gate is keyed by host **and** by a hash of the request's credential header (`Authorization`, `X-API-Key`, `Api-Key`), so two organisations calling the same API on different keys are limited separately. The gate is held only for the round trip, never during a backoff wait. Keys are documented on the [configuration reference](../../operations/application-configuration.md#webapicall).

**Automatic Citations (Perplexity & Parallel.ai):**

When the URL is a recognised Perplexity (`api.perplexity.ai/chat/completions`) or Parallel.ai (`…/v1/tasks/runs/{id}/result`) endpoint, the block automatically extracts the answer's web sources into clickable report citations — no extra configuration. See **[Automatic Citations from Web Search](web-search-citations.md)** for setup, and the [architecture page](../../architecture/citations/web-search-citations.md) for how it works.

**Use Cases:**
- External data enrichment
- API testing
- Third-party integrations
- Long-running task APIs (submit → poll status → fetch result)
- Cited web research (Perplexity / Parallel.ai)

**Limitations:**
- Only GET and POST supported
- Fixed 2-minute per-request timeout (see **Retries and rate limits** above for what is retried)
- No request body *schema* validation — a JSON-content-type POST payload is parse-checked after substitution (a malformed body fails locally), but its structure against the target API is not validated

## Financial Blocks

### Exchange Rate

Currency conversion via API.

**Purpose**: Real-time exchange rate retrieval and currency conversion

**Configuration:**

| Property | Description | Default |
|----------|-------------|---------|
| **From Currency** | Base currency code (e.g., USD, EUR) | Required |
| **Target Currencies** | One target currency code, or several separated by commas (e.g., `EUR, GBP, CHF`). Each code may be a placeholder. The first code is stored as `ToCurrency`, the rest as `ToCurrencies` | Required |
| **Operation Mode** | `GetRate` (rate only) or `Convert` (convert a value) | GetRate |
| **Exchange Date** | Historical rate date (ISO 8601 or placeholder). A date that does not parse falls back to today (UTC) | Current date |
| **Use Current Date** | Toggle to always use today's date | true |
| **Input Value** | Amount to convert: a single number, or a JSON object whose nested objects carry a `currency` field (only when Operation Mode = Convert). An empty value runs `GetRate` instead. A value that is neither a number nor a JSON object writes no output | - |

The block trims and upper-cases every target code, drops blank entries, and removes duplicates case-insensitively, keeping the first target first. A missing From Currency or first target fails the block with the error `Missing currency codes`. A failed rate lookup (for example a 403 from the rate service) also fails the block, with the error `Failed to get exchange rate: …`; the block's response then starts with `ERROR: `, so a dependant receives the no-data placeholder text instead.

**Output:**

With one target, only a JSON conversion writes a JSON response. The other modes write a raw response alone, so `{BlockName.Json...}` has nothing to read there.

| Mode and input | Raw response |
|---|---|
| `GetRate` | `<rawValue>` holds the rate (6 decimals), `<formattedValue>` reads `USD/EUR on 2026-07-07: 0.912345` |
| `Convert`, a number | `<rawValue>` and `<formattedValue>` as above, plus `<convertedValue>` |
| `Convert`, JSON | `<rates>` lists each rate used, `<convertedJson>` holds the converted JSON, which is also the JSON response |

With several targets, the block fetches one rate for each target and also writes a JSON response keyed by target code, so `{BlockName.Json.EUR}` reads the EUR result.

| Mode and input | Raw response | JSON response |
|---|---|---|
| `GetRate` | one `<rawValue_EUR>` tag per target, then `<formattedValue>` with one line per target | `{ "EUR": 0.912345, "GBP": 0.78 }` |
| `Convert`, a number | one `<convertedValue_EUR>` tag per target | `{ "EUR": 912.35, "GBP": 780.1 }` |
| `Convert`, JSON | `<rates>`, then one `<convertedJson_EUR>` tag per target | `{ "EUR": { ... }, "GBP": { ... } }`, one converted copy of the input per target |

JSON conversion converts the numbers of each object that has a `currency` string field, from that currency into the target, and sets the field to the target code. Every converted amount is rounded to 2 decimals. A source currency that equals the target converts at a rate of 1. When the input names no currency, the block fetches the From Currency rate, but no object changes.

**Use Cases:**
- Multi-currency reporting
- Financial consolidation
- Foreign exchange analysis
- Currency conversion in workflows


## Control Blocks

### Loop Start / Loop End

Iterative execution over collections.

**Purpose**: Process lists and arrays dynamically

**Configuration:**

**Loop Start:**

| Property | Description |
|----------|-------------|
| **List Title** | The name the loop items go by, e.g. `Funds` |
| **List Values Template** | The list, one item per line; usually a placeholder such as `{FundList}` |

**Loop End:**
- Marker only, no configuration

**Execution:**
1. The list resolves and splits into one item per line
2. Each item gets a loop item block tagged `{ListTitle}_{index}` (`Funds_0`, `Funds_1`, …), then a copy of every block between LoopStart and LoopEnd, tagged `{ListTitle}_{tag}_{index}`
3. Inside a copy, `{Funds}` reads that iteration's item and `{Analysis}` reads that iteration's copy; after the loop, `{Analysis}` reads every iteration's output, one per line

**Example:**
```
LoopStart: List Title "Funds", List Values Template "{FundList}"
  AiQuery (tag Analysis): Analyze {Funds}
LoopEnd
```

The mechanics, limits and failure modes: [Loop Processing](../../architecture/workflow-engine/loop-processing.md).

**Use Cases:**
- Batch processing
- Multi-item analysis
- Iterative calculations

### SwitchCase

Maps an input string to one of several predefined output values — a lookup table, not an execution router. The block does **not** branch the workflow; it emits the matched value as its response, and downstream blocks consume that value via placeholder.

**Purpose**: Normalize or translate a free-form value (often an AI Query answer) into a controlled output

**Configuration** (`SwitchCaseProperties`, executed by `SwitchCaseStrategy`):

| Property | Description |
|----------|-------------|
| **Input** | The string to match. Supports `{placeholder}` references, resolved at execution time; the resolved value is trimmed before matching. |
| **Cases** | A map of case key → output value. Keys are matched against the input **case-insensitively, after trimming**. Output values may themselves contain placeholders, resolved on emit. |
| **Default Value** | Optional fallback emitted when no case matches. If omitted and nothing matches, the block produces no output (a warning is logged, execution continues). |

**Behavior notes:**
- First matching case wins.
- Empty input or zero configured cases → no output, warning logged, no failure.
- The matched (or default) value becomes the block's response and is what downstream `{placeholder}` references resolve to.

**Use Cases:**
- Mapping a classification answer ("Invoice", "invoice ", "INVOICE") to one canonical value
- Translating a model verdict into fixed follow-up prompt text
- Choosing one of several prepared text fragments by a prior block's answer

## Utility Blocks

### Dynamic Chart

Draws a chart from data.

**Purpose**: Data visualization and charting

**Configuration:**

| Property | Description | Default |
|----------|-------------|---------|
| **Data Source JSON** | The data, usually a `{BlockName}` placeholder | None |
| **Chart Type** | Line, Bar, Column, Area, Scatter, Pie, Donut, Bubble | Line |
| **Chart Title** | Title | `Dynamic Chart` |
| **X-Axis Field** / **Y-Axis Field** | The data fields on each axis | None |
| **X-Axis Label** / **Y-Axis Label** | Axis titles | `X Axis` / `Y Axis` |
| **Series Group Field (Optional)** | Field that splits the data into series | None |
| **Color Scheme** | Default, Professional, Vibrant, Earth Tones | Default |
| **Stacking Mode** | Side-by-Side (None), Stacked, Percentage Stacked | Side-by-Side |
| **Chart Height** / **Chart Width** | Size | `400` / `100%` |
| **Data Filters (JSON Array, Optional)** | Filters applied to the data | `[]` |
| **Show Legend**, **Show Grid Lines**, **Enable Animations** | Display switches | On |

A row whose X or Y field is missing is left out. A Y value that is not a number, such as "N/A", is plotted as 0. A critical chart (**Stop the run**) leaves such a point out instead of drawing a false 0, and fails when no point is left to plot: "The Dynamic Chart block 'Revenue chart' found no numbers to plot, so it failed. Check that its data holds the X-axis field 'period' and a number in the Y-axis field 'value'."

When a report is exported to Word, each chart goes in as a picture that Chromium draws on the server with the same code as the report page. A chart that cannot be drawn goes in as a table of its data, in both builds.

**Use Cases:**
- Financial visualizations
- Trend analysis
- Performance dashboards

### Build Topics for All Documents

AI-generated topic clustering across documents.

**Purpose**: Group related content across documents

**Configuration:**

| Property | Description |
|----------|-------------|
| **Topics Template** | Optional list of topics (`TopicsModelTemplate`). With it set, the block skips finding topics and only groups the pages under these |
| RAG settings | When set, the pages the block reads; without them it reads every page of its documents |
| **Document Classes** | Filter documents by type |

There is no topic count: `BuildTopicsProperties.MaxTopics` exists in the template model but nothing reads it.

**Output**: the pages grouped under topics, as the model answers the topic-grouping prompt (`DocumentAnalysisService.GenerateTopicsFromOverviewsAsync`): first the model names topics from the pages' overviews, unless a Topics Template supplies them, then it puts each page under a topic. Both prompts are on [Tools Settings](../../features/tools-settings.md). With no pages, the block writes no output.

The block is an execution-order fence and a barrier: it runs alone, at its authored position ([fences](../../architecture/workflow-engine/parallel-execution.md#fences-pin-to-authored-position)).

**Use Cases:**
- Multi-document synthesis
- Thematic analysis
- Content organization


## Data Transformation Blocks

### Merge

The editor calls it **Apply Template To Data**. Concatenates text from earlier blocks and variables into a single output.

**Purpose**: Combine outputs of several blocks into one block of text

**Configuration:**

| Property | Description |
|----------|-------------|
| **Text Template** | Template that pulls in `{BlockName}` / `{variable_name}` placeholders |

The resolved text becomes the block's `RawResponse`, referenceable downstream like any other block output.

**Use Cases:**
- Assembling a combined prompt input from multiple sources
- Stitching section outputs together before a final AI Query

### Merge JSON

Merges several JSON inputs into one JSON value, optionally extracting snippets via XML-style tags or regular expressions before merging.

**Purpose**: Aggregate JSON produced by multiple blocks (e.g. looped AI Query results) into one structured value

**Configuration:**

| Property | Description |
|----------|-------------|
| **JSON Inputs** | The JSON values to combine (typically `{BlockName}` placeholders) |
| **Extraction Mode** | Per input: `Single` (the whole input is one JSON token), `XmlTagPattern` (the JSON inside each tag whose name matches a glob such as `page*`), or `Regex` (capture group 1 of each match). Patterns are not template-resolved |
| **Pre-Parse Transforms** | Find/replace rules applied to each input and each extracted snippet before JSON parsing (e.g. stripping digit-group separators). Not template-resolved |

An empty input, a `null` literal, or text that does not parse as JSON is skipped with a warning; the block does not fail. A critical block (**Stop the run**) with no valid input left fails instead of returning `{}`: "The Merge JSON block 'Peer spread' had no valid JSON input, so it failed. Check that the blocks feeding it return JSON." The valid tokens are then combined by `MergeTokens` in `MergeJsonStrategy`:

| Valid tokens | Result |
|---|---|
| None | `{}` |
| One | That token, unchanged |
| All objects | One object, deep-merged in input order (`JsonMergeHelpers.DeepMergeObjects`): nested objects merge, arrays under the same key concatenate, any other value is replaced by the later input's value — except that an empty string never overwrites an existing value |
| All arrays | One array: the items of every input, concatenated |
| A mix of objects, arrays, or scalars | One array whose items are the tokens themselves (nothing is flattened) |

The result is both the raw response and the JSON response, so `{tag.Json}` and `{tag.Json.field}` read it.

**Use Cases:**
- Collecting per-item JSON arrays from inside a loop into one array
- Combining per-section JSON objects into one object for a downstream chart

### String Manipulation

Applies a single string operation to an input value (Zapier-style).

**Purpose**: Reshape text without an AI call

**Operations:** Replace, Replace Using Pattern (regex), Substring, Trim, Split, Join, Change Case, Length, Escape / Unescape Special Characters, Distinct

Visible tokens (`TOKEN_NEWLINE`, `TOKEN_TAB`, `TOKEN_SPACE`, `TOKEN_RETURN`) stand in for whitespace characters so users never type backslash escapes. Case sensitivity is configurable per operation.

When the operation fails, for example on a pattern that is not a valid regular expression, the block completes with no output. A critical block (**Stop the run**) fails instead: "The String Manipulation block 'Clean ticker' could not process its input, so it failed." followed by a next step that fits the operation: "Check that its Find (regex pattern) is a valid regular expression." for a pattern that is not valid, "Its Find (regex pattern) took too long on this input. Simplify the pattern, or split the input." for one that is too slow, and "Check that its input is a JSON list of text values, such as ["a", "b"]." for **Join** A missing input completes with no output either way.

**Use Cases:**
- Cleaning or normalizing block output before a downstream block
- Splitting a delimited string into a list for a loop

## Document Processing

A block reads documents by **document class**: its class selection decides which of the run's documents reach it, and RAG settings pick pages from those. Document classes are defined per workflow ([Document Classes](../workflows/document-classes.md)). Blocks have no OCR or conversion settings: how an uploaded document is converted, OCR'd and paged is on [Document Processing](../../architecture/document-processing/index.md) and [OCR](../../architecture/ocr/index.md).

## Placeholder System

All blocks support dynamic placeholder replacement:

> [!WARNING]
> **Agent blocks use double braces**
>
> The [Agent block's](#agent) prompts render under the strict double-brace agent-template grammar — write `{{tag}}` there (autocomplete does), because the single-brace forms below stay literal inside agent prompts.

### Syntax

| Placeholder | Description |
|-------------|-------------|
| `{BlockName}` | Raw output from a specific block |
| `{variable_name}` | Workflow variable value |
| `{InputContext}` | **Not a substitution tag.** A block with RAG settings receives its retrieved pages automatically as input context; this token is passed through to the model unchanged. Configure RAG settings instead of writing it. |
| `{BlockName.fieldName}` / `{BlockName.a.b}` | A dot-path into the **raw response** when it parses as JSON; otherwise the first part is read as a tag name. When neither finds a value on a **JSON** or **JSON-in-Tag** AI Query block, the path walks the block's validated JSON, so a fenced JSON answer still resolves |
| `{BlockName.Json.fieldName}` | A field from the block's JSON response: the validated JSON of a **JSON** or **JSON-in-Tag** AI Query block, or the output of a block that always sets it (Merge JSON, Web API Call) — note the **capital `Json`**; the variant selector is case-sensitive (`{BlockName.json...}` would read the raw response instead) |
| `{BlockName.Html}` / `{BlockName.Markdown}` | A block's rendered HTML or Markdown variant (capitalised) |
| `{BlockName.tagName}` | A named value an AI Query block extracted via its output format (SingleTag / MultiTag / JsonInTag) |

<a id="object-sub-paths"></a>
**A dot-path that selects an object or array** (`{BlockName.a}` where `a` holds `{…}` or `[…]`)
substitutes it as compact JSON (`JsonStringReplacerStrategy`). Letters of every script in the Basic
Multilingual Plane (Arabic, Hebrew, Cyrillic, accented Latin, CJK) stay as written, so a model reads
names as text rather than `\u` escapes. Characters outside that plane (emoji, rare CJK) stay `\u`
escaped. HTML-sensitive characters (`<`, `>`, `&`, `'`), control characters and every invisible
format character (bidi marks, overrides and isolates, the zero-width space) stay escaped; the zero-width joiner and non-joiner stay raw, because Persian, Urdu and Indic words are spelled with them, because a
substituted value can reach report HTML through a Merge block. In a Web API Call header the object
keeps its all-ASCII form, since HTTP headers carry ASCII only. A dot-path that selects a single value
(a string or number) substitutes that value as written, and `{BlockName}` alone substitutes the whole
output verbatim; neither is re-serialized.

### Examples

**Block Output Reference:**
```
User Prompt: "Analyze the following data: {FinancialData}"
```

**Variable Reference:**
```
URL: "https://api.example.com/funds/{fund_id}/analysis"
```

**Tagged Content:**
```
Formula: {Score_Block.rating} * {Weight_Factor}
```

### Substituted-value sanitization

Properties whose value must conform to a syntax sanitize each substituted placeholder value automatically; the template's own literals are never modified. Prompts and other free-text fields are not sanitized. A substituted value that itself contains placeholders — for example a text input written as `Research {Counterparty.Name} financials` and referenced from the payload — is fully resolved first, then sanitized as one composed value.

| Syntax | Applied to | Behavior |
|---|---|---|
| URL | Web API Call: URL, Poll Completion URL | Each substituted value is percent-encoded. A template that is exactly one placeholder substitutes verbatim — the variable carries the whole URL. Either way the resolved URL may not point at an internal address (see [Web API Call](#web-api-call)). |
| JSON | Web API Call: payload · Merge JSON: inputs · JSON to Excel: input, mapping · Dynamic Chart: data source, data filters | A substituted value that lands **inside a JSON string** (`"field": "{tag}"`) is escaped as JSON string content (quotes, backslashes, line breaks, tabs) so it can't break out of the string. A placeholder in **value position** (`"field": {tag}`) substitutes verbatim — so a whole upstream JSON object can be nested as a value — as does a template that is exactly one placeholder (the value carries the whole document). A value that is **already** escaped JSON string content also passes through unchanged, and that skip is recorded as a sanitization warning in the execution state and the application log — so existing escape steps (String Manipulation "Turn line breaks into `\n`") keep working and can be removed at leisure. |
| Header value | Web API Call: Accept Header, Content Type, Additional Header values | No rewriting. A resolved value containing control characters (CR/LF) fails the block with an error naming the header. |

## Best Practices

### Block Organization

1. **Logical Flow**: Order blocks for natural data flow
2. **Clear Names**: Descriptive block names and tags
3. **Modular Design**: Reusable block configurations

### Performance Optimization

1. **Model Selection**: Turn **Use Advanced Model** off for simple tasks
2. **Caching**: Enable for repeated queries
3. **Token Limits**: Set appropriate max tokens
4. **Batches**: Use **Process Pages in Batches** for very large documents

### Error Handling

1. **Validation**: Test configurations before production use
2. **Fallbacks**: Consider default values, such as a Switch Case **Default Value**
3. **Critical blocks**: Mark a block critical when the report is worthless without it (**If this block fails** › **Stop the run**). **Set every block to stop the run** in the editor's **More actions** menu marks a whole workflow at once

### Security

1. **Confidential Variables**: Mark a variable confidential when a later reader of the run should not see its value
2. **API Keys**: Keep them in [stored keys](../../features/user-management/stored-keys.md), never in a header value or a workflow variable
3. **Data Handling**: Review counterparty data updates

## Common Patterns

### Document Analysis Workflow

```
1. Upload documents with proper classes
2. RAG block: Retrieve relevant sections
3. AI Query: Analyze extracted content (use an output format to emit named tags)
4. Reference the AI Query's tagged values downstream: {AnalysisBlock.rating}
5. Update counterparty with results
```

### Web Research Workflow

```
1. Intelligent Search: Find sources
2. AI Query: Summarize findings
3. Calculate: Compute metrics
4. Dynamic Chart: Visualize results
```

### Multi-Document Comparison

```
1. Build Topics: Generate themes across docs
2. RAG block: Retrieve relevant content per theme
3. AI Query: Comparative analysis (emit JSON via the AI Query output format)
4. Merge: Combine the per-theme outputs into one comparison
```

## Related Documentation

- [Workflow Editor Guide](../workflows/editor-guide.md) - Creating and managing workflows
- [Block Strategies](../../architecture/workflow-engine/block-strategy-pattern.md) - Technical architecture
- [Tracer Agent](../../architecture/tracer-agent/index.md) - Workflow execution analysis

