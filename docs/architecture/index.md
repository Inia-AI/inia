---
type: hub
status: current
description: "Top-level architecture hub linking to every architecture subsection: layers and containers, the workflow engine, documents, AI, tenancy, services, jobs and provenance. Read when you look for how a part of the platform works and do not yet know which page owns it."
---

# Architecture Overview

The IniaAi Platform is a .NET solution with a Vue single-page app, deployed on-premises or inside each customer's own cloud. Each link below names the page that owns the subject.

## Layers and containers

- **[Clean Architecture](clean-architecture/index.md)** — the layer split, the project references, CQRS through the in-house dispatcher, repositories and DI.
- **[System Context & Containers](c4-model/container.md)** — the users, the external systems and every container (Vue SPA, REST API, Blazor admin app, agent runtime, databases, file storage), with their technology. [C4 Model](c4-model/index.md) is its hub.

## Workflows

- **[Workflow Engine](workflow-engine/index.md)** — block-based execution, scheduling, variables, loops and shared components.
- **[ExaSearch Block](workflow-engine/exa-search-block.md)** — the web search block.


## Documents

- **[Document Processing](document-processing/index.md)** — the file-format interface, PDF, Word, Excel and email handling, and the unlock gate.
- **[File Content Inspection](document-processing/file-content-inspector.md)** — what admits a file.


- **[OCR](ocr/index.md)** — model-based page transcription.


- **[Chart Rendering](chart-rendering/index.md)** — Dynamic Chart blocks in the UI and in the Word export.
- **[File Storage](file-storage/index.md)** — where files are stored and how.

## AI and retrieval

- **[AI Integration](ai-integration/index.md)** — the model connectors, providers, auth and model routing.
- **[RAG](rag/index.md)** — chunking, overviews, embeddings and retrieval strategies.

## Tenancy and security

- **[Multi-Tenant Architecture](multi-tenant/index.md)** — organization-level data isolation.
- **[Security Model](multi-tenant/security-model.md)** — authentication and authorization.

## Services and background work

- **[Application & Services Layer](services/index.md)** — CQRS handlers, DI and the infrastructure services, including the [agent runtime integration](services/infrastructure/agent-runtime-integration.md).
- **[Background Jobs](background-jobs/index.md)** — the in-memory job service that runs workflow executions.

## Provenance

- **[Citations](citations/index.md)** and **[Web Search Citations](citations/web-search-citations.md)** — how report figures get their sources.
- **[Tracer Agent](tracer-agent/index.md)** — provenance graph and questions over it.

## Frontend

- **[Frontend (Vue SPA)](frontend/index.md)** — the user-facing Vue 3 app.

