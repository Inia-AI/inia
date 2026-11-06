---
type: guide
status: current
description: "Walks a prompt engineer through building a first workflow type with a Retrieve Data From Documents block and an AI Query block that reads its pages, running it from the editor, and reading the report. Read when you build your first workflow in the editor."
---

# Build Your First Workflow

**On this page:** Steps · A fully worked example · Done when · Stuck? · Related pages

**Persona:** a prompt engineer authoring in the workflow editor. You are not writing code and you are not calling the API — you build a workflow in the UI, run it against a document, and read the report it produces.

**Goal:** go from an empty workflow type to **a finished report you can open and read**, with one block that retrieves the right pages of a document and one block that analyses them.

**Prerequisites:**

- An account with the `WorkflowEditor` role (or `SuperAdmin`) *(a super admin grants it: [who may grant what](../../features/user-management/authorization.md#how-roles-are-created-and-assigned)).*
- Embeddings on the deployment. Step 3 picks pages by meaning, which compares the embeddings of the page overviews with your filter expression. On the [one-command Docker stack](../../getting-started/docker-stack.md) that needs a Cohere API key in `COHERE_API_KEY` ([optional keys](../../../platform/README.md#optional-keys)); without one, the retrieval block fails.
- One sample document to run against (a PDF works well — for example an annual report or a fund factsheet).
- A rough idea of the one question you want the workflow to answer about that document ("summarise the fund's strategy", "list the key financial covenants"). You will turn that into a prompt in step 4.

> [!TIP]
> **A few terms used on this page**
>
> - **[Workflow type vs. workflow vs. execution](../../reference/glossary.md#workflow-type-vs-workflow-vs-execution)** — a *workflow type* is the reusable template you build here; each time you run it against a document you produce a *workflow* run, also called an *execution*. You author the type once and run it many times.
> - **[RAG](../../reference/glossary.md#rag-retrieval-augmented-generation)** — when a block needs an answer from your document, the platform retrieves the most relevant pages first and feeds only those to the AI, instead of the whole document.

Each step links the guide that owns its detail. This page is the order and the success criteria.

## Steps

### 1. Create a workflow type

A workflow type is the template you will build. Open **Workflow Type Management** (in the editor app's left-navigation menu, at `/workflow-type-management`), click **Add Workflow Type**, and give it an ID, a name, and a category (for example ID `first-workflow`, name "My First Workflow"). The new type is granted to the organization you are working in, so its analysts can run it once you save a workflow in it. Then open **Workflow Editors** (`/editors`) and pick your new type — it opens as an empty workflow.

> [!TIP]
> **Don't see "Workflow Type Management"?**
>
> Adding a type needs the `WorkflowEditor` or `SuperAdmin` role, the same roles that edit a type's template. If the menu item isn't visible, ask a super admin to grant you the `WorkflowEditor` role.

→ [Workflow Editor → Create Workflow Type](../workflows/editor-guide.md#1-create-workflow-type) · [Workflow Types → Management Interface](../../architecture/workflow-engine/workflow-types.md#management-interface)

**Checkpoint:** your new workflow type opens in the editor as an empty workflow.

### 2. Tell users what to upload — define a document class

Before a block can read a document, the workflow needs a **document class** for it — a named bucket like "Annual Report" that users upload into. On the editor's **Document Classes** tab, add one class: give it a key, a clear **Display Name** and a short **Description** of what to upload, and set **Requirement Level** to **Required**.

On the same tab, give the workflow a page overview prompt: in the **Page overviews** card, select **Edit**, then **Use the standard prompts**, then **Save**. Step 3 finds pages through their overviews, and a page gets an overview only when the workflow has a prompt for it.

→ [Document Classes](../workflows/document-classes.md) · [Page overview prompts](../page-overview-prompts.md)

**Checkpoint:** the Document Classes tab lists one required class (e.g. "Annual Report"), and the **Page overviews** card shows **Prompt: Set**.

### 3. Add a section and a retrieval block

Workflows are organised into **sections** (groups of blocks). Add one section, then inside it add a **Retrieve Data From Documents** block. This block finds the pages of your document that match a query and outputs their text. Select the document class from step 2, click **Add Data Retrieval Settings**, set **Select The Filter Type To Use For Documents' Pages** to **Top N Pages**, and add one smart filter expression describing what to find (for example "investment strategy, objectives").

Give the block a **Replacement Tag** you will recognise, such as `strategy_pages`: that tag is how the next block reads these pages.

→ [Workflow Editor → Add Sections](../workflows/editor-guide.md#4-add-sections) · [Workflow Editor → Add Blocks](../workflows/editor-guide.md#6-add-blocks) · [Retrieve Data from Documents block](../blocks/block-reference.md#retrieve-data-from-documents-rag)

**Checkpoint:** your section contains one Retrieve Data From Documents block with your document class, a Top N Pages setting with a filter expression, and the tag `strategy_pages`.

### 4. Add an AI Query block to do the analysis

Below the retrieval block, add an **AI Query** block — this is the one that actually answers your question. In its **User prompt**, write the instruction you decided on up front and put the retrieval block's tag in braces where the pages belong. For example:

```
Using only the pages below, summarise the fund's investment strategy in three bullet points.

{strategy_pages}
```

A block reads another block's output only through its `{tag}`: when the AI Query block runs, `{strategy_pages}` is replaced by the retrieved pages' text, each page tagged with its document name and page number. Without the tag the AI Query block never sees those pages. (An AI Query block can also retrieve pages itself through its own **Add Filter Rule** settings; this guide keeps retrieval in its own block so you can see what it found.)

→ [AI Query block](../blocks/block-reference.md#ai-query) · [Block Replacement Tags](../../architecture/workflow-engine/variable-system.md#block-replacement-tags)

**Checkpoint:** your section has a Retrieve Data From Documents block followed by an AI Query block whose user prompt contains `{strategy_pages}`.

### 5. Mark the result to appear in the report

A block's result reaches the report only when you include it. On the AI Query block, click **Show in Output**, then fill the **Paragraph Sub-Title Template** field that appears (for example "Investment Strategy") so the result shows up as a titled paragraph in the finished report.

→ [Workflow Editor → Configure Blocks](../workflows/editor-guide.md#7-configure-blocks)

**Checkpoint:** the AI Query block's **Show in Output** button is filled and the sub-title is set.

### 6. Save the workflow

Click **Save**. Saving persists the template and adds an entry to its **version history**, so you can always see how it evolved and roll back — nothing to tag or commit by hand.

→ [Workflow Editor → Saving Changes](../workflows/editor-guide.md#saving-changes) · [Version History](../workflows/editor-guide.md#version-history)

**Checkpoint:** the editor shows a "Last saved" stamp and **Version history** lists your save.

### 7. Run it — attach your document and generate the report

The run controls sit above the template on the editor page:

1. Click **Upload Files**, choose your sample document, and set its document class in the picker on its row to the class from step 2.
2. Fill any variables the panel below the counterparty picker asks for.
3. Click **Generate Report**. This starts a background run (an *execution*).

→ [Workflow Editor → Running Workflows](../workflows/editor-guide.md#running-workflows-from-editor)

**Checkpoint:** the editor shows "Job started successfully." You stay in the editor; the run's status is on the **Dashboard**.

### 8. Read the report

> [!WARNING]
> **The report opens in a different app**
>
> Report output is **not** shown in this editor. It appears in **IniaAi.Web** — the Vue single-page app that end users work in, a *separate front end* served at your deployment's web address (`http://localhost:8080` on the [one-command Docker stack](../../getting-started/docker-stack.md#the-stack)). Its report list lives at `/workflow-reports`. The editor only authors the template; runs and their reports live in IniaAi.Web. See [Frontend (Vue SPA)](../../architecture/frontend/index.md) for what that application is.

Open **IniaAi.Web**, find your run in the report list (`/workflow-reports`), wait for the execution to finish, and open the finished report. Find the paragraph under the sub-title you set in step 5 — that is your AI Query block's answer, generated from the pages your retrieval block found.

→ [Reading a Report](../workflows/reading-a-report.md) — run-status labels, following a citation to its source page, and exporting the result.

**Checkpoint:** you are looking at the generated report.

## A fully worked example

The steps above are deliberately generic so they fit any document. Here is **one concrete, end-to-end fill-in** so you can see exactly what real values look like — a two-block section that reads a fund's annual report and writes one paragraph about its strategy. Copy these as a starting point and adjust to your document.

**Document class (step 2)**

| Field | Value |
|-------|-------|
| Display Name | `Annual Report` |
| Description (shown to the uploader) | `The fund's most recent annual report, as a PDF.` |
| Requirement Level | `Required` |

**Block 1 — Retrieve Data From Documents (step 3)**

| Field | Value |
|-------|-------|
| Replacement Tag | `strategy_pages` |
| Document classes | `Annual Report` |
| Select The Filter Type To Use For Documents' Pages | `Top N Pages` |
| Smart Filter Expression Template | `investment strategy, objective, approach, how the fund invests` |
| Number Of Pages To Use Template | `3` |
| Start Index Template / End Index Template | empty (the whole document) |

This finds the three pages most relevant to the fund's strategy and outputs their text. Left empty, **Number Of Pages To Use** takes one page.

**Block 2 — AI Query (steps 4–5)**

*System prompt* (the role and the rules):

```
You are an analyst summarising a fund's investment strategy for an internal
one-page brief. Use only the pages provided. Be specific and factual. If the
strategy is not stated in the pages, say "Not stated in the provided pages."
Do not add outside knowledge.
```

*User prompt* (the actual instruction, with the retrieved pages pulled in by their tag):

```
From the pages below, summarise the fund's investment strategy in exactly three
bullet points: (1) what it invests in, (2) its stated objective, and (3) its
approach to risk. Keep each bullet to one sentence.

{strategy_pages}
```

*Output settings:* click **Show in Output** and set **Paragraph Sub-Title Template** to `Investment Strategy`.

**What the finished report shows (step 8)**

When you run this against a real annual report, the paragraph under your sub-title comes back looking like this (your wording will differ — the model writes from *your* document):

> ### Investment Strategy
>
> - The fund invests primarily in investment-grade corporate bonds and short-dated government debt across developed markets.
> - Its stated objective is to deliver steady income with capital preservation, targeting returns modestly above its benchmark over a full market cycle.
> - It manages risk through broad issuer diversification, a duration kept close to the benchmark, and limits on any single sector or counterparty.

Three sentences, each grounded in the three pages your retrieval block found — not general knowledge. That is the whole loop: retrieve the right pages, ask one clear question about them, label the answer so it lands in the report.

## Done when

You opened the finished report in IniaAi.Web and read the paragraph your AI Query block produced — a real answer grounded in the document you uploaded. From here you add more blocks, more sections, and more document classes to grow it into a full analysis.

## Stuck?

- **The answer ignores your document or says nothing was provided** — check that the AI Query block's user prompt contains the retrieval block's tag in braces, spelled exactly as the **Replacement Tag** field shows it (tags are case-sensitive).
- **The block ran but cited or used the wrong pages** — that is a retrieval problem; walk it back with [Debug a retrieval problem](debug-retrieval.md).
- **Your run is missing from the IniaAi.Web report list** — the list can be narrowed to the workflow types chosen for your user; see [Workflow type visibility](../../features/user-management/workflow-type-visibility.md).
- **A variable, block, or document class is not behaving** — see the Troubleshooting and Common Issues sections of the [Workflow Editor guide](../workflows/editor-guide.md#common-issues) and the [Workflow Guides index](../workflows/index.md).
- **The retrieval block fails and says it needs a Cohere API key**: the deployment has no embeddings; see the prerequisites.
- **Anything access-related** (you cannot see the editor pages, cannot upload, no workflow types listed) — an admin of your deployment owns that; [Authorization](../../features/user-management/authorization.md) says which role opens which page.


## Related pages

- [Reading a Report](../workflows/reading-a-report.md) — how to read, trace, and export the report you just produced.
- [Workflow Editor Guide](../workflows/editor-guide.md) — every panel and control in the editor.
- [Block Reference](../blocks/block-reference.md) — every block type and its settings.
- [Document Classes](../workflows/document-classes.md) — what users upload and how blocks read it.
- [Page overview prompts](../page-overview-prompts.md): the prompts the workflow summarises document pages with, which its search blocks read.
- [Workflow Guides](../workflows/index.md) — the broader index of workflow how-tos.
