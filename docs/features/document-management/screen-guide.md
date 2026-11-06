---
type: guide
status: current
description: "Walks through uploading, processing, marking static, and reading each page beside its original on the Documents pages, and how to have a document read again. Read when you upload documents outside a workflow, inspect what the platform read from a page, or need a document's text read afresh."
---

# Documents screen

**On this page:** Where to find it · The list · Inspecting a document · Having a document read again · Related pages

The **Documents** pages are where you upload documents, process them, and inspect exactly what the platform extracted, summarised, and embedded for each page. They are the pages you reach for when a citation points at the wrong page or an overview looks wrong, because they show the per-page **text, overview, OCR state and embedding** that retrieval actually runs against — and they are where you have a document **read again** after an OCR change.

The editor app has no document screen: `/documents-management` and `/document-viewer/{DocumentId}` are gone.

## Where to find it

- **Top navigation → Documents.**
- **Routes:** `/documents` (the list) and `/documents/:id` (one document).

The documents listed are scoped to your **currently selected organization** — switch organizations from the org picker if the list looks empty or unexpected.

## The list

The list shows each document's **name, type, category, uploading user, Static mark, size and created date**. Search, the type, category and user filters, sorting and paging run on the server. A `?jobId=` link narrows the list to the documents of one workflow job. Each row has actions:

- **Preview** — the original file, for PDFs and images.
- **Download** — the original file.
- **Edit** — mark the document static, or clear the mark (see below).
- **Delete** — remove the document and its stored file, after a confirmation. Only `Admin`, `Operator` and `SuperAdmin` see it; select several rows to delete them together.

### Upload and process

**Upload documents** opens a dialog. Choose one or more files, give each one a **category** (one of the document categories editors manage at `/document-category-management`, or **None**), then click **Upload and process**. The dialog takes the files one at a time: it stores the file, files it under its category, and queues processing, then moves to the next file while the queued ones are read. Each row shows its own state — **Uploading** with a percentage, **Processing**, then **Processed** or **Failed** with the reason. You can close the dialog early; processing goes on. The list refreshes once every chosen file is stored.

Processing here extracts the text and runs OCR on scanned pages. It calls models, so it costs money. It writes no page **overview**: overviews come from a workflow run, with the overview prompt of the document's class or the workflow's default ([Page overview prompts](../../guides/page-overview-prompts.md)). The overviews and vectors an earlier run wrote stay, so the next run of that workflow reuses them; a page that holds an overview but no vector is embedded. Legacy Word files (`.doc`) are stored but never read into pages; their row reads **Stored, not read into pages**.


A file whose name and size match a document already stored in your organization is not stored twice: the upload returns that document, with the pages it already has.

### Static documents

A **static** document joins every run of one workflow type in your organization, under one document class, without being uploaded again. **Edit** opens a dialog with a **Static document** switch. Turn it on, choose the **workflow type** and the **document class** — the list offers only the input classes of that type's workflow — and save. Turn it off and save to clear the mark. The **Static** column names the workflow type of a static document.

## Inspecting a document

Opening a document gives its header actions — **Preview** (PDFs and images), **Download** (the original file) and **Download text** (every page's text as one `.txt` file, one `<pageN>…</pageN>` block per page). Under the name sit the file type, size, upload date, category and uploader; **Details** opens the rest of the metadata (class, workflow type, extension, description, topics, table of contents, sources).

Below them, the page reader shows how the platform turned the original file into its own pages. On the left, the **page list** gives each page's number, the part of the source page it came from (**full page**, **left half** or **right half**, when a two-up scan was split) and an icon for how its text was read. The search box narrows the list to the pages whose text or overview holds the words, ignoring case; the search runs on the server.

On the right is the selected page:

| Part | What it shows |
|------|---------------|
| Heading | **Page N of M**, the file page and half it came from, and how the text was read: **Text layer** (the file's own text), **OCR**, **OCR unreliable, read as an image**, **OCR failed**, or **Waiting for OCR**. |
| **Original** | The page as uploaded, rendered from the file and cropped to its half. Click it to open it at full size. A Word or Excel page has no image, and says so. |
| **Text** | The page's text **As stored** — exactly what the platform keeps and what a block reads — or **Formatted**, with its Markdown tables drawn. **Copy** copies the stored text. When OCR judged the page unreliable, the text panel says the model reads the page as an image through its overview. |
| **Overview** | The AI-generated summary of the page — the text that gets embedded for retrieval — with its own **Copy**. |
| **Sections** | The section titles the document's table of contents gives the page, when it has one. |
| **Embedding** | The embedding model and its number of dimensions, or **No embedding yet**. |

**‹** and **›**, or the left and right arrow keys, step through the pages. The address carries the page (`?page=12`), so a link opens the same page. This is the ground-truth view when you are diagnosing why a page did or did not match a query: put the original beside the text and check what was read.

## Having a document read again

A change to a workflow's overview prompt needs no action: the next run of that workflow sees that the document's overviews came from another prompt and writes them again.

Processing a document again — **Upload and process** with the same file, or `POST /api/v1/documents/{id}/process` ([Documents API](../../reference/api/documents.md)) — reads only the pages that are not read yet, such as a page whose OCR failed. A page that already has its text keeps it, so a change to the deployment's OCR model or settings does not reach a document that was read before it. To have the whole document read again:

1. Go to **Documents** (`/documents`), and note the document's **category** and whether it is **Static**.
2. **Delete** the document (an `Admin`, `Operator` or `SuperAdmin` action). This removes the document and its stored file.
3. Click **Upload documents**, choose the file, give it the same category, and click **Upload and process**. Wait until its row reads **Processed**.
4. If it was static, **Edit** it and set the **Static document** switch, workflow type and document class again.

Re-run the workflow: the run writes fresh overviews and vectors for the new document and retrieves against them.

**Done when** the document's page reader shows each page's text as the current OCR reads it, and the workflow's next run cites the new document.

> [!TIP]
> If overviews are garbled across the *whole* document rather than just imprecise, re-processing alone will not fix it — the problem is upstream text extraction or OCR, which is **deployment configuration**, not an in-app knob. See [Debug a retrieval problem](../../guides/end-to-end/debug-retrieval.md).
>
>

## Related pages

- [Debug a retrieval problem](../../guides/end-to-end/debug-retrieval.md) — the runbook that sends you here to inspect overviews and re-process a document.
- [Citations Architecture](../../architecture/citations/index.md) — how the page content shown here is matched back to facts in a report.
- [Overview Generation](../../architecture/rag/overview-generation.md) · [Embedding Generation](../../architecture/rag/embedding-generation.md) — how the overview and vector you see per page are produced.
- [Page Image API](../../reference/api/page-image.md) — the endpoint that renders these pages as images for citation previews.
