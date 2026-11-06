---
type: hub
status: current
description: "Hub of numbered end-to-end journeys by goal — build a workflow, integrate against the API, debug retrieval or a stuck job, add a block or frontend feature — and the contract every journey follows. Read when you start cold and need the whole sequence of steps for one goal."
---

# End-to-End Guides

The rest of this site documents the platform **piece by piece** — each API endpoint, each architecture subsystem, each config key on its own page. That is the right shape when you already know what you are looking for. It is the wrong shape when you are starting cold and the real question is *"what is the whole sequence of steps to get from nothing to done?"*

These guides are that missing layer. Each one is a **numbered path from zero to a verified success state** for a single goal. They do not re-explain the subsystems — they link into the reference pages at each step and tell you exactly when you are finished ("Done when you see X").

## I want to…

| Goal | Start here | You're done when |
|------|-----------|------------------|
| **Build a workflow in the editor and get a report out (no code)** | [Build your first workflow](build-your-first-workflow.md) | You open the report your workflow produced and read its answer. |
| **Integrate a service against the API and get a generated report back** | [API: zero to result](api-zero-to-result.md) | You parse a finished report tree, including a citation. |
| **Fix a retrieval problem (the AI cited the wrong page)** | [Debug a retrieval problem](debug-retrieval.md) | You have identified the one RAG knob to change. |
| **Diagnose a run that won't finish ("it's stuck")** | [Debug a stuck job](debug-stuck-job.md) | You can name the cause: queued, slow, stalled, or lost on restart. |
| **Add a feature to the end-user frontend (Vue SPA)** | [Contributing a Frontend Feature](../../architecture/frontend/contributing-a-feature.md) | Your feature ships through the frontend repo's CI. |
| **Add a new workflow block type** | [Block Implementation Guide](../../architecture/workflow-engine/block-implementation-guide.md) | Your block runs in a workflow and has tests. |



Most of these are full zero-to-done journeys that follow the contract below. A few (the frontend-feature and block-type guides) live as focused guides elsewhere in the site — they are linked here so this hub stays the single "I want to…" entry point.

## How these guides are written

Every guide on this page follows the same contract, so you always know what you are reading:

- **Persona + goal + prerequisites** are stated up front. If you do not match the persona, you are probably on the wrong guide.
- **Steps are numbered** and each links the reference page that owns the detail. The guide threads the steps together; the reference page is the source of truth for any one step.
- **It ends at an explicit "Done when…"** — a state you can observe, not a vague "you should now be able to…".
- **Placeholders** (anything you must supply — a base URL, a tenant id, a secret) are written as `<like-this>` with a one-line note on who provides the value.
- **Stuck? →** every guide ends with a **Stuck?** section that names the likely causes and where to go next, rather than dead-ending on "ask the team".

## Related pages

- [Guides](../index.md) — the broader tutorial index (workflows, blocks, page overview prompts).
- [API Reference](../../reference/api/index.md) — every endpoint, indexed.
- [Architecture](../../architecture/index.md) — every subsystem, indexed.

