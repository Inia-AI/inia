---
type: hub
status: current
description: "Hub page for the C4 model, linking to the system context/container diagram and per-subsystem component docs. Read when you look for the platform's architecture diagrams."
---

# C4 Model Architecture

The [C4 model](https://c4model.com/) visualizes software architecture at increasing levels of detail: context → containers → components → code.

For the IniaAi Platform, the context and container views live on **one page**:

- **[System Context & Containers](container.md)** — who uses the platform and which external systems it touches, then the applications/data stores inside it and their technologies.

Component-level detail lives with each subsystem instead of separate C4 diagrams: [Workflow Engine](../workflow-engine/index.md), [RAG](../rag/index.md), [Multi-Tenant](../multi-tenant/index.md), [AI Integration](../ai-integration/index.md).
