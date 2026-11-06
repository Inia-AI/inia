---
type: explanation
status: current
description: "Page-grouping chunking for AI Query blocks: when it runs, the fixed-count and bin-packing chunkers, the table-of-contents case, the 100-image cap per request, and the settings' trade-offs. Read when a block runs once per batch of pages, or you set its chunking properties."
---

# Chunking

Once [retrieval](index.md) has selected the pages a block should see, those pages still have to fit into a single model call. Chunking is the step that groups the selected pages into batches the model can process. A block whose context is small enough runs once; a block whose selected pages exceed the configured limit runs once per chunk, and the results are combined.

This is **page-grouping** chunking: the unit is a whole page, and a chunk is a set of pages — not a sub-page passage. Retrieval already happens at page granularity (see [RAG architecture](index.md)), so chunking never splits a page in half.

## When chunking happens

Chunking is opt-in per block, controlled by the AI-query block's properties (`AiQueryProperties`, `platform/IniaAi.Domain/Entities/Workflows/Blocks/Properties/`). It runs only when `ShouldChunkLargeInputContexts` is on **and** the block has RAG settings and documents (`AiQueryStrategy`):

| Property | Default | Effect |
|----------|---------|--------|
| `ShouldChunkLargeInputContexts` | `false` | Master switch. When off, all selected pages go to the model in one call. |
| `ChunkingMode` | `FixedPageCount` | Which chunker to use (see below). |
| `ChunkSize` | `20` | Pages per chunk when mode is `FixedPageCount`. |
| `MaxChunkCharacters` | `50000` | Character budget per chunk when mode is `OptimalCharacters`. |
| `PreservePageOrder` | `true` | When mode is `OptimalCharacters`, keep pages in document order rather than packing for fewest chunks. |

`ChunkerFactory.Create(AiQueryProperties)` (`platform/IniaAi.Core/Workflows/Chunking/`) reads these properties and returns the matching `IPageChunker`. The chunker is then handed the selected pages and returns a dictionary of named chunks, each chunk being the pages it contains keyed by their original index.

## Chunking modes

### Fixed page count

`FixedCountChunker` is the default. It walks the selected pages in order and emits a new chunk every `ChunkSize` pages (a simple stride). With the default `ChunkSize` of 20, a 55-page selection becomes three chunks of 20, 20, and 15 pages. Page order is always preserved.

Use this when pages are roughly uniform in length and you want predictable, evenly sized batches.

### Optimal characters (bin-packing)

`OptimalChunker` packs pages by **character budget** instead of page count, so a chunk holds as many pages as fit under `MaxChunkCharacters` (a page's size is its `Text` length). This produces fuller, more even batches when page lengths vary widely — a run of short pages stays together instead of wasting a fixed-count slot.

It has two packing modes:

- **`PreservePageOrder = true` (default)** — *first-fit sequential*. Pages are added to the current chunk in document order until the next page would exceed the budget, then a new chunk starts. Page order is preserved.
- **`PreservePageOrder = false`** — *first-fit decreasing*. Pages are sorted largest-first and each is placed into the first chunk with room. This minimizes the **number** of chunks but does not preserve document order.

A single page larger than the whole budget becomes its own chunk rather than being dropped.

### Table-of-contents retrieval

When one of the block's RAG settings is `UseTableOfContents`, the configured chunker is not used: each group the retrieval returned (one per table-of-contents section) is its own chunk.

### The image cap

After the chunker, `ImageCapChunker.CapByImageCount` splits any chunk that carries more than 100 page images (`RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST`, the model APIs' per-request image limit) into consecutive chunks of at most 100 images each, in page order. A block that does not chunk still never sends more than 100 page images in one request: the content builder attaches page images up to the cap, OCR-image-only pages first.

```csharp
IPageChunker chunker = ChunkerFactory.Create(props);
Dictionary<string, Dictionary<int, Page>> chunks = chunker.ChunkPages(selectedPages);
```

## Knobs that affect retrieval quality

- **Bigger chunks, fewer model calls.** Raising `ChunkSize` or `MaxChunkCharacters` sends more pages per call — cheaper and faster overall, but the model has to attend to more context at once, which can dilute precision on long documents.
- **Smaller chunks, more focused calls.** Lower limits keep each call tight, but the block runs more times and the model never sees the whole picture in one pass, which can hurt cross-page reasoning.
- **Order matters for narrative documents.** Keep `PreservePageOrder = true` whenever the document reads top-to-bottom (filings, reports). Turn it off only when pages are independent records and you care more about fitting fewer, fuller chunks.
- **Page length drives mode choice.** Uniform pages → `FixedPageCount` is simplest. Highly variable page lengths → `OptimalCharacters` avoids both half-empty and over-stuffed chunks.

## There is no sub-page chunk

Retrieval and embedding operate at the **page** level: the page overview is the embedded unit, and "chunking" in the RAG and AI-query path always means grouping whole pages. The platform stores no sub-page passage. A `Chunk` entity and its `UserData.Chunks` table once existed for that purpose; no code ever wrote a row, and the `RemoveChunks` migration dropped the table.

## Related pages

- [RAG architecture](index.md) — retrieval selects the pages that chunking then batches.
- [Embedding generation](embedding-generation.md) — why retrieval is page-level in the first place.
- [Retrieve Data from Documents block](../../guides/blocks/block-reference.md#retrieve-data-from-documents-rag) — the author-facing settings that drive chunking.
- [Workflow engine](../workflow-engine/index.md) — how a block that produces multiple chunk results is executed and combined.
