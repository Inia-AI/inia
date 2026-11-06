---
type: reference
status: current
description: "Defines core platform terms in plain language: counterparty (shown on screen as Company), RAG, overview, citation, workflow type, run, block, section, document class, report, signature field, tenant. Read when a term in another page is unclear or you need the word the platform uses for a concept."
---

# Key Terms

**On this page:** Counterparty · RAG (Retrieval-Augmented Generation) · Overview (page overview) · Page Chunking · Citation · Filter Expression · Workflow type vs workflow vs execution · Three-tier model · Block · Document class · Report · Signature field · Section · Organization / tenant · Related pages

Plain-language definitions of the core concepts used throughout this documentation. The terms are written for everyone — prompt engineers, analysts, and integrators — not only developers.

## Counterparty

The business entity a piece of analysis is *about* — typically the company, fund, or borrower under review. A counterparty is created once and then reused: when you start a workflow you can attach the counterparty it concerns, and the platform keeps the execution, its documents, and its report linked to that entity. Peer companies (used for comparison) are also counterparties.

Every screen of both apps, and every message a person reads, calls a counterparty a **Company** (plural **Companies**): the analyst app's page is **Company Management**, its menu entries are **Companies** and **Company types**, and the admin app's labels read **Select Company** and **Allowed Company Types**. Counterparty stays the name in code, in routes such as `/counterparty-management`, in API paths and JSON fields, in error codes such as `Counterparty.NotFound`, and in workflow placeholders such as `{Counterparty.Name}`. The word lives in one place per app: `terms.json` in the analyst app ([Internationalization](../architecture/frontend/i18n.md#shared-terms)) and `CounterpartyTerms` in the platform.

See the [Counterparties API](api/counterparties.md) for how they are created and listed.

## RAG (Retrieval-Augmented Generation)

A technique that finds the most relevant passages from the uploaded documents and hands *only those passages* to the AI model as context, instead of the whole document set.

**Why it precedes an AI Query:** language models can only consider a limited amount of text at once, and feeding them an entire data room is both impossible and noisy. RAG runs first to retrieve the handful of pages or chunks that actually answer the question, so the following AI Query reasons over focused, on-point evidence rather than everything at once. This is what keeps answers grounded in the source material and lets the model cite specific pages.

See [RAG Architecture](../architecture/rag/index.md), [Chunking](../architecture/rag/chunking.md), and [Embedding Generation](../architecture/rag/embedding-generation.md) for the mechanics.

## Overview (page overview)

A short, AI-written summary of a single document page — a one-to-two-sentence description plus a few structured fields (title, keywords, note headers). The platform embeds the *overview*, not the raw page text, into the vector that RAG searches, so retrieval quality depends directly on overview quality. Overviews are generated once during document processing, after text extraction and OCR and before embedding.

See [Overview Generation](../architecture/rag/overview-generation.md).

## Page Chunking

The step that groups the pages RAG selected into batches small enough for one model call. This is *page-grouping* chunking — the unit is a whole page and a chunk is a set of pages, never a sub-page passage. A block whose selected pages fit runs once; one whose pages exceed the limit runs once per chunk, and the results are combined.

See [Chunking](../architecture/rag/chunking.md).

## Citation

A reference that ties a piece of AI-generated text back to its source — a specific document page, a web URL, or the output of an earlier block. Citations are stored as structured data on each report paragraph (not embedded as XML in the text); the content carries lightweight `<fact id="cX">…</fact>` markers that link to the citation records, so the report preserves provenance through the whole execution.

See [Citations Architecture](../architecture/citations/index.md).

## Filter Expression

A short, natural-language search phrase a RAG block uses to find the pages it should read — for example `"financial performance, revenue growth"`. Retrieval strategies such as *Top N Pages* match each filter expression semantically against the document overviews and return the closest pages. They are author-time configuration on a block, not something an integrator supplies at run time.

See [Block Reference → Top N Pages](../guides/blocks/block-reference.md) and [RAG Architecture](../architecture/rag/index.md).

## Workflow type vs workflow vs execution

These three words are easy to confuse because they describe the same thing at three different stages.

- **Workflow type** — the stable identity of a kind of analysis (e.g. `CorporatesCreditReport`). It is the string ID you reference from the API and the label an analyst picks from a list. Many runs share one workflow type. The Vue app calls it a report type.
- **Workflow (template)** — the authored definition behind a workflow type: its sections, blocks, variables, document classes, and prompts. This is what a prompt engineer edits. It lives in the **Core** side of the platform.
- **Execution** — a single *run* of that template against specific documents and inputs. The pages for readers of the open edition call it a run, and the API calls it a workflow execution. Each execution is an immutable historical record: a snapshot of what the template looked like at run time plus everything the run produced. Executions live in the **UserData** side of the platform and are never edited as templates.

> A workflow type is the *name*, the template is the *recipe*, and an execution is *one meal cooked from that recipe*.

See the [Workflow Executions API](api/workflow-executions.md) and [Workflow Engine](../architecture/workflow-engine/index.md).

## Three-tier model

Older pages describe three tiers: template, configured instance and execution instance. Two of them are stored:

1. **Template** — the canonical, editable definition of a workflow, in Core.
2. **Executed copy** — when a run starts, the platform copies the template into a new workflow that belongs to the run (`WorkflowExecutionOrchestrator`). The copy, stored in UserData, takes the run's inputs, collects its results and is never edited as a template.

The "configured instance" is that copy before its blocks run, not a separate record. The same entity classes serve both stored forms; what changes is the meaning, a blueprint and a finished record.

## Block

The smallest unit of work in a workflow. A block performs one step — for example an **AI Query** block sends a prompt to the model, a Web API Call block fetches data, and a chart block renders a visual. Blocks declare what they consume (documents, variables, the output of earlier blocks) and produce a single output that later blocks and the final report can reference by its replacement tag.

See [Block Implementation Guide](../architecture/workflow-engine/block-implementation-guide.md).

## Document class

A kind of document that a workflow reads, such as an annual report. A workflow names the classes it reads, and a user tags each upload with its class.

See [Workflow Engine](../architecture/workflow-engine/index.md).

## Report

What a run writes: the draft that a reader reviews, edits and confirms in the Vue app. Each output block of the workflow gives a paragraph of the report, and each section of the workflow a section of the report.

See [Reading a Report](../guides/workflows/reading-a-report.md).

## Signature field

A named value that the prompts of a workflow use, as a `{Name}` token. Each organization can set its own value, through a proposal that other users approve.

See [Signature field overrides](../features/signature-overrides.md).

## Section

A named group of blocks within a workflow. Sections organize a workflow into logical stages for the editor and the report, but they do not set the order blocks run in: the engine schedules a run of sections as one dependency graph, so blocks that do not read each other's output run at the same time, across sections. A loop or parameterized section runs on its own, and a section whose skip gate reads output that earlier sections produce starts a new run of sections (`DispatchSegments`).

See [Whole-workflow parallel DAG execution](../architecture/workflow-engine/parallel-execution.md).

## Organization / tenant

An **organization** is a customer workspace — the boundary that owns users, documents, executions, and counterparties. **Tenant** is the same concept seen from the multi-tenancy machinery: every tenant-scoped API request carries an `X-Tenant-Id` header naming the organization it acts on, and the platform scopes the request's data to that organization. A user can belong to several organizations and switch between them; the words *organization* and *tenant* are used interchangeably in this documentation.

See [Multi-Tenant Architecture](../architecture/multi-tenant/index.md) and the [Authentication API](api/authentication.md) for how tenant access is enforced.


## Related pages

- [API Reference](api/index.md) — the full endpoint index.
- [Workflow Executions](api/workflow-executions.md) — the lifecycle of an execution.
- [RAG Architecture](../architecture/rag/index.md) — how retrieval grounds AI Query blocks.
- [Multi-Tenant Architecture](../architecture/multi-tenant/index.md) — how organizations isolate data.
