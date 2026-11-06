---
type: guide
status: current
description: "How to find, read, and export a finished report in IniaAi.Web: the report list and its status labels, the report's sections, tracing a citation to its source page, checking the JSON data a run sends to other systems, and the Actions menu. Read when you open a run's report, wonder what its status means, check a run's data output, or need to export or re-run it."
---

# Reading a Report

**On this page:** Where reports open · Run-status labels · Reading the finished report · Following a citation to its source · Checking the data a run sends to other systems · Downloading and exporting the result · Done when · Related pages

**For:** anyone who runs a workflow and needs to read the result — a prompt engineer checking their own run, or an analyst working through a finished report. You are not writing code; this page is about the **finished report**: where it opens, how to tell a run is done, how to follow a citation back to the page it came from, and how to download the result.

Reports live in **IniaAi.Web** — the web application your team works in day to day, served at your deployment's web address (`http://localhost:8080` on the [one-command Docker stack](../../getting-started/docker-stack.md#the-stack)). The workflow editor only *authors* the template; every run and its report appears here.

> [!TIP]
> **A few terms used on this page**
>
> - **[Execution](../../reference/glossary.md#workflow-type-vs-workflow-vs-execution)** — one *run* of a workflow against specific documents. A report is what an execution produces; the two words are often used interchangeably here.
> - **[Citation](../../reference/glossary.md#citation)** — a marker in the report text that ties a fact back to its source: a specific document page, a web link, or an earlier step's output.

## Where reports open

Open IniaAi.Web and go to the **Reports** list at `/workflow-reports`. Every run your team has started appears here as a row — unless an administrator has narrowed which report types you see (below) — newest first, with its **report type**, the **company** it is about, who started it, when, and its current **status**. You can filter the list by report type, company, or user.

**Not seeing a report a colleague ran?** An administrator can give each person a shortlist of report types, so your list stays free of report types you never work with. It is a decluttering setting, not a permission — a report outside your shortlist still opens normally from a direct link, and the shortlist never adds anything you couldn't already see. Ask an administrator to adjust yours in [workflow type visibility](../../features/user-management/workflow-type-visibility.md).

The list refreshes itself every few seconds, so a run you just started will move through its statuses without you reloading the page.

**Click any row to open that report** — including a failed one, because the detail page is where you see what went wrong.

## Run-status labels

The status on each row (and the pill at the top of the open report) tells you where the run is. You will see one of these:

| Status | What it means |
|--------|---------------|
| **Queued** | Submitted and waiting for a worker to pick it up. |
| **Processing documents** | Reading and parsing the documents you uploaded. |
| **Processing** | Generating the report — running the blocks and asking the model. |
| **Ready to review** | Finished. The output is ready for you to read and check. |
| **Processed** | A reviewer has marked the report as final. |
| **Failed** | The run hit an error. Open it and check **View Logs** for the reason. |
| **Cancelled** | Someone stopped the run before it finished. |

**Queued**, **Processing documents**, and **Processing** are in-flight — wait for them to settle. **Ready to review** is the one you are usually waiting for: the report is complete and readable. **Processed** is the same report after a reviewer has confirmed it.

## Reading the finished report

Open a **Ready to review** report and you get a structured brief, not a wall of text. It is organised into the **sections** and titled **paragraphs** the workflow author defined — for example "Investment Strategy", "Key Figures", "Risks" — each written from the documents you uploaded. A side navigation lets you jump between sections. Tables and charts a workflow produces render inline.

The header of the open report shows the run's status, the report type, and the **documents** that went into it — both the files you uploaded and any files the run produced. You can preview or download each of those source documents from there.

## Following a citation to its source

This is the part that makes the report auditable: most facts in the text are **clickable citation markers**. A cited phrase is highlighted inline — click it.

- **Click a fact** and a small popover lists its sources. A document source reads as **"Document name – page 4"**; a web source shows the link.
- **Click a document source** in that popover and a **page preview** opens, showing the *actual page of the actual document* the fact was drawn from. That is the proof the number or claim is grounded in your source, not invented.
- **Click a web source** and it opens in a new browser tab.

Some facts are *synthesized* from earlier steps rather than taken directly from a page. Their popover shows **Drawn from**: the pages and web links that the earlier steps cited, with any source that states the fact in bold at the top. A long list shows its first few; **Show all** opens the rest. Click one to open its page preview or link, as above. Under the list, the name of the earlier step and a **Trace sources** action open a slide-over **source-chain panel**, where you walk back through the chain of earlier outputs step by step. Reports created before this list existed show only the step's name and the trace action.

A fact with no traceable source is shown plainly and is not clickable — there is nothing to open.

> [!TIP]
> **If a citation points at the wrong page**
>
> That is a retrieval-tuning problem, not a broken report — the workflow author can adjust which pages the relevant block reads. Walk it back with [Debug a retrieval problem](../end-to-end/debug-retrieval.md).

## Checking the data a run sends to other systems

Some workflows also send **data** to other systems beside the report: a JSON that another system reads through the API ([Get Report Data Outputs](../../reference/api/reports.md#get-report-data-outputs)). Such a report shows a **Data** entry at the end of the section list, with one entry per output under it. Open one to see the JSON itself, indented, as the other system receives it. The browser reads it first, so a number written `250000.00` shows as `250000`, and keys that are whole numbers, such as years, list in ascending order; the API itself returns the JSON unchanged.

- Each cited figure in the JSON is a citation marker, as in the report text: click it to see its sources and open the page preview.
- **Copy JSON** puts the JSON on the clipboard; **Download** saves it as `<name>.json`.
- The data is what the run produced. Editing the report does not change it, and the Word and Markdown exports leave it out.

## Downloading and exporting the result

Open the report and use the **Actions** menu at the top right:

- **Export as DOCX** — downloads the report as a Word document, named for the report, that you can share, edit, or attach. This is the export most people want.
- **Export as Markdown** — downloads the same report as one Markdown (`.md`) file, named the same way: plain text that another tool can read or convert. Each chart becomes a table of its data, and the file holds no pictures and no citation markers ([what the file holds](../../reference/api/reports.md#markdown-export-md)).
- **View Logs** — opens the per-step execution log; the first place to look when a run **Failed** or behaved unexpectedly.
- **Edit & Re-run** — runs the same workflow again, useful after the template or a document changed. When the workflow changed since the original run, it asks you to fill in any new fields first.

A section labelled **Not in exports** beside its heading shows in the app but is left out of both exported files: its author kept it for reading on screen only. If an export fails, the page says so in a message and downloads nothing.

The menu also holds **Cancel run** while the run is still in flight, **Feedback** for [report feedback](../../features/report-feedback.md), and **Delete** once the run has settled.

To save the **source files** themselves rather than the report, use the preview/download controls next to each document in the report header.

> [!NOTE]
> **"Download workflow" is a developer tool**
>
> If you see a **Download workflow** item in the Actions menu, that downloads the raw executed-workflow data for debugging and only appears for operators (`Operator` or `SuperAdmin`). It is not the report — for a shareable result use **Export as DOCX** or **Export as Markdown**.

## Done when

You opened a **Ready to review** report, read the paragraphs under their sub-titles, clicked a fact to confirm it traces back to a real source page, and exported the result as a DOCX or a Markdown file.

## Related pages

- [Build your first workflow](../end-to-end/build-your-first-workflow.md) — produce the report you are now reading.
- [Debug a retrieval problem](../end-to-end/debug-retrieval.md) — when a citation points at the wrong page.
- [Frontend (Vue SPA)](../../architecture/frontend/index.md) — what IniaAi.Web is, for the technically curious.
- [Source Chain, Citations & Tracer (UI)](../../architecture/frontend/source-chain-and-citations.md) — how citations render under the hood.

