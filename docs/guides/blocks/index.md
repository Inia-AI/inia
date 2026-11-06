---
type: hub
status: current
description: "Hub for workflow blocks: what a block is, the block types by category with a link to each one's reference entry, and the architecture and feature guides behind them. Read when you are choosing a block type or looking for the page that documents one."
---

# Block Catalog

**On this page:** What is a block? · Block Categories · How a block runs · For developers · Feature Guides · Related pages

## What is a block?

A **block** is a single step in a workflow — one unit of work that takes some input and produces a result. If a workflow is the recipe for a report, blocks are the individual instructions: "ask the model to summarise these documents", "pull the three most relevant pages from the annual report", "convert this table to a spreadsheet", "look this company up on the web".

A block can read the output of other blocks (using a `{BlockName}` placeholder) and hand its own result to later blocks. You never write code to do this — you pick a block type, fill in its settings, and the platform runs it.

A block has three things you care about as an author:

- **A type** — what the block does. See the categories below.
- **Settings** — the configuration specific to that type. See the [Block Reference](block-reference.md) for every setting.
- **A replacement tag** — the name other blocks use to pull in this block's output, written `{TagName}`.

## Block Categories

The editor's names are in bold; each links to its entry on the [Block Reference](block-reference.md).

### Core Blocks
- **[AI Query](block-reference.md#ai-query)** — ask the model, with optional document context and output format
- **[Calculate Formula](block-reference.md#calculate-number)** — evaluate a formula

### AI Blocks
- **[Retrieve Data From Documents](block-reference.md#retrieve-data-from-documents-rag)** — pull the relevant pages of the run's documents
- **[Intelligent Search](block-reference.md#intelligent-search-exasearch)** — web search through Exa
- **[AI Agent](block-reference.md#agent)** — a skill-driven agent run on the agent runtime


### Data Blocks
- **[Web API Call](block-reference.md#web-api-call)** — HTTP calls, with optional polling
- **[Exchange Rate](block-reference.md#exchange-rate)** — currency rates and conversion


### Control Blocks
- **[Loop Start / Loop End](block-reference.md#loop-start--loop-end)** — repeat the blocks between them once per list item
- **[Switch Case](block-reference.md#switchcase)** — map an input value to one of several predefined outputs (a lookup table, not a branch)

### Utility and Transformation Blocks
- **[Dynamic Chart](block-reference.md#dynamic-chart)** — a chart from JSON data
- **[Build Topics For All Documents](block-reference.md#build-topics-for-all-documents)** — topics across the run's documents
- **[Apply Template To Data](block-reference.md#merge)** (Merge) — combine text from several blocks
- **[Merge JSON](block-reference.md#merge-json)** — combine JSON inputs into one value
- **[String Manipulation](block-reference.md#string-manipulation)** — replace, split, trim, case and other text operations


JSON to XLSX, Peer Financials and Verification run only in the paid build ([what the open build shows](block-reference.md#blocks-that-only-the-paid-build-runs)), and Agent and Verification appear only where the agent runtime is enabled.

### [Communication Blocks](communication-blocks.md) (planned)
Email Send and Webhook Call — **not implemented yet**; the page describes planned functionality.

## How a block runs

How the engine orders and runs blocks, and what a block can read: [Execution Context](../../architecture/workflow-engine/execution-context.md) and [Whole-workflow parallel DAG execution](../../architecture/workflow-engine/parallel-execution.md). The `{placeholder}` syntax: [Placeholder System](block-reference.md#placeholder-system).

## For developers

- [Block Strategy Pattern](../../architecture/workflow-engine/block-strategy-pattern.md) — the interface, registration, execution context, and the catalog of concrete strategies
- [Block Implementation Guide](../../architecture/workflow-engine/block-implementation-guide.md) — how to add a new block type
- [Execution Context](../../architecture/workflow-engine/execution-context.md) — what a block can read at run time

## Feature Guides

- **[AI Query Output Format](ai-query-output-format.md)** — validate and retry an AI Query response against a structured format, and reference the extracted values downstream
- **[Automatic Citations from Web Search](web-search-citations.md)** — Web API Call answers from Perplexity / Parallel.ai get clickable citations in the report

## Related pages

- [Block Reference](block-reference.md) — complete configuration guide for every block
- [Workflow Editor Guide](../workflows/editor-guide.md) — building and running workflows
- [Glossary](../../reference/glossary.md) — plain-language definitions of counterparty, RAG, workflow type, and more

