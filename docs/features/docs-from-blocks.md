---
type: explanation
status: current
description: "Explains how a block creates a document during a run: which block does (Intelligent Search), how the document is stored, paged and added to the run's pages, how CreatesDocumentClass targets it at later blocks, and which outputs are never paged. Read when a later block should read what an earlier block produced, or a created document does not reach a block."
---

# Documents from Blocks

Block strategies can **create documents during workflow execution**. A document created by a block is automatically processed like an upload — pages extracted, overviews and embeddings generated — and becomes available to downstream blocks for RAG in the same run.

## How it works

- A strategy returns a `BlockStrategyOutput` (`platform/IniaAi.Core/Workflows/Strategies/BlockStrategyOutput.cs`) whose `Documents` list declares any documents it produced.
- After the block executes, `WorkflowHandler.ProcessBlockStrategyOutput` stores each document with the run's job (its name gets the job id as a suffix, and its bytes go to the organization's file storage), runs document processing on it, and adds its pages to the execution's corpus (`AllPages`), so a later RAG block can retrieve from them. The Intelligent Search document is the exception to storage: it is processed and paged for the run but never written to file storage. An editor block or section test, which has no job, stores nothing.
- Document processing pages only the media types `MediaTypeExtensions.ProducesPages` names: PDF, JPEG, PNG, CSV, Excel (`.xls`, `.xlsx`, `.xlsm`), Word `.docx`, plain text and Markdown. Any other created document, such as a `.docm`, is stored but adds no pages.

## Blocks that create documents

| Block | Document created | Notes |
|-------|------------------|-------|
| **Intelligent Search (ExaSearch)** | Text document of the search results, split into pages by the platform setting `IniaAi.WordCountPerPage`; each `Page` carries the result's `SourceUrl` | Created with `ShouldBeDisplayedInOutput = false` — the document feeds RAG, it does not surface in the report. Pages are numbered 1..N in the order the results are written, so a page-number filter or an exact-pages rule addresses one page |


## Targeting the document at downstream blocks: `CreatesDocumentClass`

Document-producing block properties implement `IProducesDocumentBlockProperties` (`platform/IniaAi.Domain/Entities/Workflows/Blocks/Properties/Abstractions/`), which exposes the nullable `CreatesDocumentClass`:

- Set it to the document class the created document should carry (e.g. `web-research`).
- A downstream RAG block filtering by that document class then retrieves from the created document — the standard way to chain "search the web → analyze the results with RAG".
- A block that reads the class waits for every block that creates it, even when it is authored first. The rule, including why `Any` readers wait for nothing, is on [Parallel execution](../architecture/workflow-engine/parallel-execution.md#building-the-graph).

Implementing classes: `IntelligentSearchProperties`, `Json2XlsxProperties`.

**Web API Call creates no document.** The block has no output document class: `WebApiCallProperties` has no `CreatesDocumentClass`, so a value stored by an older save is dropped on the next save. A later block reads the response through `{tag}`.

## Related

- [ExaSearch Block](../architecture/workflow-engine/exa-search-block.md) — the search block's full configuration
- [Block Strategy Pattern](../architecture/workflow-engine/block-strategy-pattern.md) — `BlockStrategyOutput` in the strategy contract

