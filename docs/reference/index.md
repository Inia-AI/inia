---
type: hub
status: current
description: "Router page linking to the API reference, the database schema pages and the glossary. Read when you look for an endpoint, a table or a platform term and do not know which page holds it."
---

# Reference Documentation

Exhaustive lookup pages: every API route, every database table, and the platform's terms.

## API Reference

Start at the [API Reference](api/index.md): the full route table with role gates, the request conventions (tenant header, OData, errors) and the OpenAPI document.

### API — Core
- [Authentication](api/authentication.md): the token endpoint, its grants, MFA and the token claims
- [System Users (Provisioning)](api/system-users.md): service accounts for machine clients
- [Discovery](api/discovery.md): the workflow types, document classes and counterparty types a client can use
- [Users](api/users.md): user management and the current user's tenants

### API — Workflows
- [Submit Workflow Execution](api/submit-workflow-execution.md): the end-to-end submit walkthrough
- [Workflow Executions](api/workflow-executions.md): starting, tracking and cancelling runs
- [Reports](api/reports.md): a run's report, its export and feedback
- [Background Jobs](api/background-jobs.md): job status
- [Executed Workflow Download](api/executed-workflow-download.md): one executed workflow as a `.workflow` file


### API — Documents & Data
- [Documents](api/documents.md): document listing, detail, edits and deletes
- [Files](api/files.md): upload and download
- [Page Image](api/page-image.md): a rendered page of a document
- [Counterparties](api/counterparties.md): counterparties and their import


## Database Schema

- [Database Schema Overview](database/index.md): the two schemas, tenant scoping, providers and migrations
- [Core Database Schema](database/core-database.md): templates and deployment-wide configuration
- [User Data Database Schema](database/user-data-database.md): organizations, users and everything a run produces

## Glossary

- [Glossary](glossary.md): platform terms and where each is defined

## Quick Start

### For API Integration
1. [Authenticate](api/authentication.md)
2. [Discover available tenants](api/users.md#get-current-users-tenants)
3. [Submit a workflow execution](api/submit-workflow-execution.md) or [manage counterparties](api/counterparties.md)

### For Platform Development
1. Review [Clean Architecture](../architecture/clean-architecture/index.md) patterns
2. Implement [new blocks](../architecture/workflow-engine/block-implementation-guide.md) following established patterns
3. Understand [multi-tenant](../architecture/multi-tenant/index.md) data isolation
