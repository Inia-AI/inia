---
type: explanation
status: planned
description: "Describes the planned Email Send and Webhook Call blocks, which do not exist (no such block type), and how to call a webhook today with a Web API Call block. Read when you need a workflow to send an email or call a webhook."
---

# Communication Blocks

> [!WARNING]
> **PLANNED BLOCKS**
>
> The blocks described in this section (Email Send, Webhook Call) are planned features and have not been implemented yet: `BlockTypes` has no email, webhook or notification type.

Communication blocks enable external notifications and webhooks for workflow integration.

## Available Blocks

Communication blocks are planned to provide:

- **Email Send**: Send email notifications from workflows
- **Webhook Call**: Trigger external webhooks with workflow data
- **Notification Services**: Integration with messaging platforms

## Coming Soon

Full documentation for communication blocks will be added as these features are implemented in the platform.

## Current Alternatives

For external API integration, you can currently use:

### Web API Call Block

Use the Web API Call block for calling external APIs and webhooks:

**[Web API Call Documentation →](block-reference.md#web-api-call)**

For detailed configuration, see **[Block Reference →](block-reference.md#web-api-call)**

**Key Features:**
- GET and POST methods
- Custom headers
- JSON payloads
- Async API support

**Example Webhook Usage:**
```
URL: https://hooks.example.com/workflow-complete
Method: POST
Payload: {"job_id": "{JobId}", "status": "completed"}
```

`{JobId}` is a reserved tag the engine fills with the run's job id ([reserved tags](../../architecture/workflow-engine/variable-system.md#substitution-sources)). A Web API Call block runs when the scheduler reaches it, not when the workflow completes, so make its payload reference the outputs it reports (`{tag}`): the scheduler then runs it after those blocks.

## Related Documentation

- [Block Reference](block-reference.md) - Complete block configuration guide
- [Workflow Editor Guide](../workflows/editor-guide.md) - Using the editor
- [Block Strategies](../../architecture/workflow-engine/block-strategy-pattern.md) - Technical architecture
