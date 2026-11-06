---
type: hub
status: current
description: "Site homepage routing to the task guides, the open edition's starting pages, the two front ends, architecture, guides, features, reference, operations and compliance. Read when you arrive at the docs and do not yet know which section holds your answer."
---

## I want to… (task guides)

New here, or just need to **get something done**? Start with an end-to-end journey — each one threads the scattered reference pages into a single, numbered, zero-to-done path with an explicit "Done when…" success state.

| I want to… | Start here |
| --- | --- |
| Build a workflow (no coding) and see a real report come out | [Build Your First Workflow](guides/end-to-end/build-your-first-workflow.md) |
| Read and download a finished report | [Reading a Report](guides/workflows/reading-a-report.md) |
| Add a new workflow block type (developer) | [Block Implementation Guide](architecture/workflow-engine/block-implementation-guide.md) |
| Run the platform from a clone and sign in as admin | [Quickstart](getting-started/quickstart.md) |
| Configure AI (Bedrock) for a production deployment | [Customer-Owned Bedrock Setup](operations/customer-bedrock-setup.md) |
| Integrate against the API — from no credentials to a parsed report | [API: Zero to Result](guides/end-to-end/api-zero-to-result.md) |
| Debug a stuck background job that never finishes | [Debug a Stuck Job](guides/end-to-end/debug-stuck-job.md) |
| Figure out why the AI cited the wrong page | [Debug Retrieval](guides/end-to-end/debug-retrieval.md) |
| Find where a piece of code actually lives | [Solution Layout](getting-started/solution-layout.md) |


See the full set in the **[End-to-end Task Guides hub](guides/end-to-end/index.md)**. The rest of this site is piece-by-piece reference — use the routing below once you know what you're looking for.

## The open edition

These pages say what the platform is, how to run it from the open repository, and how to change it. Read them in this order when you are new.

| Page | Read it to |
|---|---|
| [Platform Overview](getting-started/platform-overview.md) | learn what the platform does, what a run produces and the words the pages use |
| [Quickstart](getting-started/quickstart.md) | go from a clone to a report with cited figures, with one command |
| [Build from source](../platform/README.md#prerequisites-to-work-on-the-source) | build the solution, run its tests and run the apps without Docker |
| [The open build and the full edition](getting-started/editions.md) | see, area by area, what the open build holds and what the full edition adds |
| [Platform Tour](architecture/platform-tour.md) | find your way through the projects and follow a run from upload to report |
| [Add a Block Type](guides/blocks/add-a-block-type.md) | add a block type in your own project, with no change to the platform |
| [Security](getting-started/security.md) | prepare an install for production, and learn how sign-in, roles and organizations work |
| [The Docker Stack](getting-started/docker-stack.md) | see the images, the services of `compose.yaml`, how secrets reach them, and the compose check |
| [Replace a File-Format Implementation](architecture/document-processing/file-formats.md) | see the document interfaces and the free implementation, and put your own in their place |
| [Contributing](../CONTRIBUTING.md) | report a bug, ask for a feature, or offer a change |
| [Report a vulnerability](../SECURITY.md) | report a security problem in private |

## Two front ends

The platform ships **two distinct user interfaces**, each with its own audience and technology:

| Front end | Technology | Who uses it | Docs |
| --- | --- | --- | --- |
| **IniaAi.WebApp** | Blazor Server (MudBlazor) | Admins & prompt engineers — workflow authoring and platform administration | [Workflow Authoring Guides](guides/index.md) |
| **IniaAi.Web** | Vue 3 SPA (`iniaai/web` nginx image) | End users — running workflows and reviewing results | [Frontend (Vue SPA)](architecture/frontend/index.md) |

## Find your way around

The documentation is organized by **what you're trying to do**:

| If you want to… | Go to |
| --- | --- |
| Set up the project, build it, or contribute code | [Getting Started](getting-started/platform-overview.md) |
| Understand how the platform is built | [Architecture](architecture/index.md) |
| Work on the Vue single-page frontend | [Frontend (Vue SPA)](architecture/frontend/index.md) |
| Author workflows or look up a block | [Guides](guides/index.md) |
| Learn what a product capability does | [Features](features/index.md) |
| Look up an API endpoint or DB table | [Reference](reference/index.md) |
| Deploy, operate, or secure the platform | [Operations](operations/index.md) |
| Review where a deployment's data goes | [Compliance](compliance/data-flows-and-subprocessors.md) |


## Popular destinations

### 🏗️ Architecture
- [Workflow Engine](architecture/workflow-engine/index.md) - Block-based execution system
- [Multi-Tenancy](architecture/multi-tenant/index.md) - Multi-organization architecture and data isolation
- [AI Integration](architecture/ai-integration/index.md) - Anthropic, AWS Bedrock, and Azure AI Foundry
- [RAG](architecture/rag/index.md) - Retrieval-Augmented Generation strategies
- [Frontend (Vue SPA)](architecture/frontend/index.md) - Vue 3 single-page app, components, state, and build
- [Application & Services Layer](architecture/services/index.md) - CQRS, DI, and infrastructure services

### 📘 Guides
- [Build Your First Workflow](guides/end-to-end/build-your-first-workflow.md) - Zero-to-report, no coding
- [Debug a Stuck Job](guides/end-to-end/debug-stuck-job.md) - Runbook for a run that never finishes
- [Workflow Editor Guide](guides/workflows/editor-guide.md) - Build your first workflow
- [Block Reference](guides/blocks/block-reference.md) - Configure every block type
- [Block Catalog](guides/blocks/index.md) - Core, AI, Data, Control, and Utility blocks


### 📚 Reference
- [API Reference](reference/api/index.md) - REST API endpoints
- [Database Schema](reference/database/index.md) - Core and UserData databases

### ⚙️ Operations
- [Customer-Owned Bedrock Setup](operations/customer-bedrock-setup.md) - Wire AI to the customer's AWS account
- [Application Configuration](operations/application-configuration.md) - Every setting, with placeholders


---

## The platform in one list

Each line links to the page that owns the subject:

- [Clean Architecture](architecture/clean-architecture/index.md) — the .NET 10 solution's layers, and [Solution Layout](getting-started/solution-layout.md) for the projects on disk.
- [Workflow Engine](architecture/workflow-engine/index.md) — block-based workflow execution.
- [Multi-Tenancy](architecture/multi-tenant/index.md) — organizations, isolation, and the deployment models.
- [AI Integration](architecture/ai-integration/index.md) — the chat and embedding providers.
- [Document Processing](architecture/document-processing/index.md) and [OCR](architecture/ocr/index.md) — reading uploaded files into pages.
- [RAG](architecture/rag/index.md) — retrieval over a run's documents.
- [Background Jobs](architecture/background-jobs/index.md) — asynchronous processing.
- [Application & Services Layer](architecture/services/index.md) — the in-house CQRS dispatcher and the shared services.
- [Frontend (Vue SPA)](architecture/frontend/index.md) — the end-user app.


## Contributing

To report a bug, ask for a feature, or offer a change, follow [Contributing](../CONTRIBUTING.md).

