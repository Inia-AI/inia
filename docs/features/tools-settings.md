---
type: reference
status: current
description: "The prompt sets on the admin app's Tools Settings page: what each tab holds, which step reads it, when an edit takes effect, and how a deployment gets its first copy. Read when you change a deployment-wide prompt outside a workflow's blocks, such as the Excel analysis, Build Topics or OCR prompts."
---

# Tools Settings

**Tools Settings** (`/tools-settings` in the admin app) holds the prompts that steps outside a workflow's own blocks read. Each tab is one prompt set. A deployment holds one copy of each set, and every workflow of every organization reads it, so one edit changes them all. Every tab says so in its header, in one shared sentence (`ToolsSettingsScopeNotice`): `One edit changes every workflow for every organization.` Who may open the page: [Authorization](user-management/authorization.md).


Each set is a table whose newest row wins at read time. The Excel analysis set is read once per workbook and the Topics set once per **Build Topics For All Documents** block, so an edit takes effect from the next one; a failed read uses the code defaults. Startup adds a row with the code defaults when no row carries the date of the current defaults. **Save Changes** writes the tab's edits; the toolbar also downloads the set as JSON and uploads one.

| Tab | Prompts | The step that reads them |
|---|---|---|
| OCR | `PdfOcrPrompts` | The OCR of scanned pages ([OCR architecture](../architecture/ocr/index.md)). |
| Excel analysis | `ExcelAnalysisPrompts`: the layout prompt and three header prompts, each a system and a user prompt | `ExcelSheetAnalysisService`, which reads each spreadsheet before cutting it into pages: first the sheet's layout, then the header rows for that layout (one table; several tables without shared columns; several tables with shared columns). In the user prompts `{0}` is a sample of the sheet, and in the shared-columns header prompt `{1}` is the shared columns. |
| Topics | `TopicsPrompts`: topic finding and topic grouping, each a system and a user prompt | The **Build Topics For All Documents** block. Topic finding follows the page overviews and names the topics; a block with a **Topics Template** skips it. Topic grouping follows the overviews and puts each page under a topic; in its user prompt `{0}` is the list of topics. |
| Reconciliation | `ReconciliationPrompts`: the generic find-match and compare-lists prompts | `ReconciliationService`, which asks a model to match counterparty names, read once per run. |
| Use Auto-Detected ToC | `UseAutoDetectedTableOfContentsPrompts` | The auto-detected table-of-contents retrieval. |
| Graph RAG | `GraphRagPrompts` | Questions about where a report's data came from. |




Page overview prompts are not here: each workflow holds its own ([Page overview prompts](../guides/page-overview-prompts.md)).

The Excel analysis and Topics sets came from the retired Prompt Library. The startup migration that retired it copied the library's general Excel and topic prompts over the defaults, so a deployment that edited them keeps its text. It did not carry a prompt the library held for one workflow type or document category; it logs each one it drops ([Overview generation](../architecture/rag/overview-generation.md#where-the-prompts-lived-before)).
