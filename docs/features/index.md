---
type: hub
status: current
description: "Features hub linking to every feature page: workflow management, documents, and user management and access, with pointers to the architecture pages. Read when you look for how a user-facing feature behaves and do not yet know which page owns it."
---

# Platform Features

Each link below names the page that owns the subject. How a subsystem works inside lives under [Architecture](../architecture/index.md); the API surface is the [API reference](../reference/api/index.md).


## Workflow management

- [Workflow Template Versioning](workflow-template-versioning.md) — the version history of workflow templates
- [Editor Agent](editor-agent.md) — agent mode in the workflow editor
- [Editor Agent Evals](editor-agent-evals.md) — running and grading the editor agent's eval set
- [Workflow GitHub Save](workflow-github-save.md) — mirroring workflow templates to a GitHub repository
- [Tools Settings](tools-settings.md) — the prompt sets on the admin app's Tools Settings page
- [Signature field overrides](signature-overrides.md) — per-organization signature fields and their review queue
- [Documents from Blocks](docs-from-blocks.md) — documents that blocks create during a run
- [Report Feedback](report-feedback.md) — comments on a finished report

## Documents

- [Documents screen](document-management/screen-guide.md) — uploading, processing and reading documents, and having one read again
- [Page Image API - Frontend Integration](document-management/page-image-api-integration-on-frontend.md) — how the Vue app shows a page's image


## User management and access

- [User management](user-management/index.md) — the overview of users, organizations and access
- [Authorization](user-management/authorization.md) — the role catalog and the role gate of every surface
- [Workflow Type Visibility](user-management/workflow-type-visibility.md) — the per-user shortlist of workflow types
- [Stored Keys](user-management/stored-keys.md) — per-organization API keys for Web API Call blocks
- [Multi-factor authentication](user-management/mfa.md) — the second sign-in step
- [OIDC SSO](user-management/sso-oidc.md) — sign-in through an identity provider

## Platform capabilities under Architecture

- [AI Integration](../architecture/ai-integration/index.md) — model providers and routing
- [Background Jobs](../architecture/background-jobs/index.md) — how runs are queued and executed
- [File Storage](../architecture/file-storage/index.md) — where document bytes live
- [Multi-tenant architecture](../architecture/multi-tenant/index.md) and [Data isolation](../architecture/multi-tenant/data-isolation.md) — organizations and how their data is kept apart
- [Tracer Agent](../architecture/tracer-agent/index.md) — provenance questions about a run's output


