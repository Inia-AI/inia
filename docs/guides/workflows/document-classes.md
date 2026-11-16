---
type: guide
status: current
description: "How to define, edit, and delete a workflow's document classes in the editor, point each block at the classes it reads, and what the pre-run check refuses or warns about, with its exact messages. Read when you add a document class, a block reads the wrong documents, or a run is refused for a missing or extra document."
---

# Document Classes

**On this page:** What a document class controls · Managing document classes in the editor · Using document classes in blocks · What gets checked before a run · Older workflows · Done when · Related pages

A **document class** is a named bucket for the documents a workflow expects — for example "Annual Report", "Bond Prospectus", or "Financial Statements". Defining document classes lets you tell users what to upload, decide which uploads are mandatory, and route each document to the blocks that should read it.

> [!TIP]
> **A few terms used on this page**
>
> - **[Workflow type](../../reference/glossary.md)** — the reusable template you author in the editor.
> - **[RAG](../../reference/glossary.md)** — when a block needs an answer from your documents, the platform retrieves the most relevant pages first. Document classes let a block retrieve only from the right documents.

## What a document class controls

Each class has a few simple settings you choose in the editor:

| Setting | What it does |
|---------|--------------|
| **Key** | The class's identifier (e.g. `documentClass_1`). Blocks name a class by its key. |
| **Display Name** | The name users see when they upload (e.g. "Annual Report"). |
| **Description** | A short note telling users exactly what to upload for this class. |
| **Document Category** | Optional; stamped on the documents of this class. |
| **Requirement Level** | Whether a document of this class is mandatory, recommended, or optional (see below). |
| **Limit to one document** | When on, users may upload only a single document of this class. |
| **Is for output documents** | Marks a class the workflow *produces* rather than one users upload. Output classes accept no uploads. |
| **Made by** | For a class users upload: the output of another workflow that a file of this class is, such as the spread an earlier workflow makes. Type part of a name to search the list. Empty means any file will do. An output class has no Made by. The open platform records the link and shows it in the editor; it does not check files against it. |

### Requirement levels

| Level | Effect when no document of this class is uploaded |
|-------|---------------------------------------------------|
| **Required** | The workflow cannot run until at least one document of this class is provided. |
| **Suggested** | The user sees a warning, but the workflow can still run. |
| **Optional** | No check — the document is entirely optional. |


## Managing document classes in the editor

### Where to find them

1. Open your workflow type in the Workflow Editor.
2. Select the **Document Classes** tab. Its **Document Classes** card lists every class defined for the workflow.

### Adding a class

1. Click the **+** button in the Document Classes card.
2. In the dialog:
   - Enter a **Key** and a **Display Name**.
   - Add a **Description** to guide users on what to upload.
   - Add a **Description for the model** when the platform's own models must tell this class apart from the others. Nobody outside the platform sees it. Write what qualifies a document as this class, the near-miss classes it is not (an interim report is not an annual report), and the cues on the page that decide it. Of the platform's own models, only the full edition's document intake screening reads it.
   - Pick a **Requirement Level** (Required, Suggested, or Optional).
   - Turn on **Limit to one document** if only one should be allowed.
   - When files of this class come from another workflow, pick that workflow's output under **Made by**. The list holds the output classes of the current version of every other workflow type, without system types and retired ones.
   - Under **Page overview prompt**, give the class its own prompt for summarising its pages, or leave it empty to use the workflow's default. See [Page overview prompts](../page-overview-prompts.md).
3. Click **Save**.

### Editing and deleting

- Click the **Edit** (pencil) icon to change a class.
- Click the **Remove** (trash) icon to delete one, and confirm.
- An output class shows **Used by**: the input classes whose Made by names it, in the current version of each workflow type.
- When the workflow or output an input is made by no longer exists, the dialog warns and keeps the link until you choose another output or clear the field.
- When the step that makes that output does not stamp its file, the dialog warns: `The step that makes <workflow> · <output> does not stamp its file, so a run that checks this input refuses the file. Turn the stamp on in that step.`
- The link travels with the workflow: in its exported file, in an import, in a workflow bundle to another deployment, and in its version history. It names the maker by its workflow type id, and a bundle imported from the editors list keeps that id. Until the maker reaches the other deployment, the dialog shows the warning that it no longer exists.

> [!WARNING]
> Deleting a document class also removes it from every block in the workflow that uses it.

## Using document classes in blocks

Once a class exists at the workflow level, you can tell each block which documents to read. In the block's configuration, open **Select Document Classes** and choose:

- **Specific classes** — the block reads only documents of the classes you pick.
- **Any (All document classes)** — the block reads every uploaded document.
- **None (No documents)** — the block reads no documents.

When the workflow runs, each block receives only the documents that match its setting. This is how one block can analyse "Invoices" while another works on "Receipts" in the same run.

A class can also be created during the run, by a search or spreadsheet block. A block that reads a specific class waits until every block that creates it has finished, wherever it sits in the workflow. A block set to **Any** reads the uploaded documents and does not wait. Press **Dependency chain** on a block to see what it waits for ([Workflow Editor Guide](editor-guide.md#seeing-what-a-block-depends-on)).

## What gets checked before a run

When a user starts a workflow, the platform checks the uploaded documents against your classes and either blocks the run or shows a warning:

| Situation | Result | Message |
|-----------|--------|---------|
| A **Required** class has no documents | Blocks the run with an error | `Required document class '<name>' is missing` |
| A **Limit to one document** class has more than one document | Blocks the run with an error | `Only one '<name>' document is allowed, but <n> were uploaded` |
| An output class has uploads | Blocks the run with an error | `Output document class '<name>' should not have uploaded documents` |
| A **Suggested** class has no documents | Shows a warning, run still allowed | `Suggested document class '<name>' is missing` |
| An **Optional** class has no documents | No check | — |

`<name>` is the class's display name, so the user can fix the upload and try again.

## Older workflows

Workflows authored before document classes lived at the workflow level keep their classes inside individual blocks. They keep working:

- When a workflow defines no document classes of its own, the pre-run check treats every class its blocks name as **Optional** with no single-document limit (`DocumentClassValidationService`).
- Nothing moves those classes to the workflow level on import or save. To make one required, or to offer it in the upload pickers, add it on the **Document Classes** tab with the same key the blocks use. Once the workflow defines any class, the check reads only the workflow's classes.

## Done when

The **Document Classes** tab lists each class the workflow expects, with the requirement level you intend, every block reads the classes it should, and a test run with the documents you expect raises no class error.

## Related pages

- [Workflow Editor Guide](editor-guide.md) — full tour of the editor, including the Document Classes card.
- [Block Reference](../blocks/block-reference.md) — how individual blocks consume documents.
- [Glossary](../../reference/glossary.md) — workflow type, counterparty, RAG.
- [Document Processing](../../architecture/document-processing/index.md) — for developers: how documents are parsed, OCR'd, and indexed.
- [Workflow Engine Architecture](../../architecture/workflow-engine/index.md) — for developers: the `Workflow.DocumentClasses` collection and a block's `SupportedDocumentClasses`.
