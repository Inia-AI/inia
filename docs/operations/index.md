---
type: hub
status: current
description: "Operations index routing operators to every operations page: deployment from release images, Bedrock setup, configuration, first login, internal service calls and data management. Read when you look for the operations page that owns a task and do not know its name."
---

# Operations

**On this page:** Deployment model · Deployment — customer self-deploy path · Infrastructure · Data Management · Related Documentation

This section holds the operational guidance for deploying, running and maintaining the IniaAi Platform.

An operator who runs the platform **in their own environment** from pre-built release images follows [Release Package Deployment](release-package-deployment.md) → [Application Configuration](application-configuration.md) → **[First Login](first-login.md)**. For the full guided journeys (deploy → first login, etc.) see the [End-to-End Guides](../guides/end-to-end/index.md).


## Deployment model

What leaves a deployment and to whom: [Data Flows & Subprocessors](../compliance/data-flows-and-subprocessors.md).


## Deployment — customer self-deploy path

These are the pages a **customer operator** follows, in order:

- **[Release Package Deployment](release-package-deployment.md)** - What a release contains, how to obtain the images, the secrets and TLS you supply, and bring-up on one Docker host
- **[Customer-Owned Bedrock Setup](customer-bedrock-setup.md)** - Pointing the platform at the customer's own AWS Bedrock: AWS mode, the IAM policy, the region, console model access and the AI smoke test
- **[Application Configuration](application-configuration.md)** - Every runtime configuration key, with placeholder values
- **[First Login](first-login.md)** - The final step of a deployment: the seeded admin accounts, the login URLs and the signed-in check
- **[Calling an Internal Service from a Workflow](internal-service-calls.md)** - The process before any workflow may call a service inside the deployment's own network


## Infrastructure


### Azure


- **[Azure AI Foundry Integration](../architecture/ai-integration/azure-foundry.md)** and **[Platform Integration](../architecture/ai-integration/azure-platform-integration.md)** - How the platform calls Claude and Cohere on Azure


## Data Management

- **[Soft-Delete Cleanup](soft-delete-cleanup.md)** - The weekly hard delete of expired soft-deleted records, per organization


## Related Documentation

- [Architecture Overview](../architecture/index.md)

