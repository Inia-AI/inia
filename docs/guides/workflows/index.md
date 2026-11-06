---
type: hub
status: current
description: "Hub for authoring workflows: links to the editor guide, the first-workflow walkthrough, document classes, overview prompts, reading a report, and the architecture pages behind variables, tags, loops, retrieval and models. Read when you are about to author or change a workflow and need the page that covers one part of it."
---

# Workflow Guides

Learn how to create, configure, and manage workflows in the IniaAi Platform. This page only links; each linked page owns its facts.

> [!TIP]
> **A few terms used throughout these guides**
>
> - **[Workflow type vs. workflow vs. execution](../../reference/glossary.md)** — a *workflow type* is the reusable template you author (for example "Credit Analysis"). A *workflow* run, also called an *execution*, is one run of that template against specific documents and inputs.
> - **[Counterparty](../../reference/glossary.md)** — the company or entity a report is about (a fund, an issuer, a borrower); workflows can read its saved data and write results back to its profile.
> - **[RAG](../../reference/glossary.md)** (retrieval-augmented generation) — the platform retrieves the most relevant pages of your documents and feeds only those to the AI, instead of the whole document.

## Start here

- [Build your first workflow](../end-to-end/build-your-first-workflow.md) — from an empty workflow type to a finished report, with a worked example.
- [Workflow Editor Guide](editor-guide.md) — every panel and control in the editor: types, sections, blocks, variables, saving, version history, import and export, test runs.

## Build and configure

- [Block Reference](../blocks/block-reference.md) — every block type and its settings; [Blocks hub](../blocks/index.md) to browse them by category.
- [Document Classes](document-classes.md) — what users upload, which blocks read it, and what the pre-run check refuses.
- [Page overview prompts](../page-overview-prompts.md) — the prompts that summarise each page for the search steps.
- [Signature overrides](../../features/signature-overrides.md) — per-organization prompt values on a shared template.
- [Shared components](../../architecture/workflow-engine/shared-components.md) — Library sections and blocks reused across templates.


## Run and read

- [Reading a Report](reading-a-report.md) — the report list, status labels, citations, and export.
- [Workflow Template Versioning](../../features/workflow-template-versioning.md) — the version history behind every save.


## How it works underneath

- [Workflow Engine Architecture](../../architecture/workflow-engine/index.md) — the workflow → section → block model and how a run executes.
- [Parallel execution](../../architecture/workflow-engine/parallel-execution.md) — which blocks wait for which, and what runs at the same time.
- [Variable System](../../architecture/workflow-engine/variable-system.md) — variables, variable and block replacement tags, confidential values.
- [Loop processing](../../architecture/workflow-engine/loop-processing.md) — how a loop expands its body per item.
- [Parameterized sections](../../architecture/workflow-engine/parameterized-sections.md) — one section expanded per variant.
- [RAG Architecture](../../architecture/rag/index.md) — the retrieval strategies and how pages are selected.
- [AI providers](../../architecture/ai-integration/providers.md) — how a block's model choice resolves on each deployment.
- [Citations Architecture](../../architecture/citations/index.md) — how a fact in a report is tied to its source.
- [Workflow Types & Categories](../../architecture/workflow-engine/workflow-types.md) — types, categories, and which organizations may use a type.

## When something goes wrong

- [Workflow Editor Guide → Common Issues](editor-guide.md#common-issues) — variables not appearing, blocks not running, an import that fails.
- [Debug a retrieval problem](../end-to-end/debug-retrieval.md) — a report cites the wrong page or misses a fact.
- [Debug a stuck job](../end-to-end/debug-stuck-job.md) — a run never finishes.
- [Tracer Agent](../../architecture/tracer-agent/index.md) — analysing what an execution did.
