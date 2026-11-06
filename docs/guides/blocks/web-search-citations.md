---
type: guide
status: current
description: "Walks through configuring a Web API Call block against Perplexity or Parallel.ai so the answer's web sources become clickable report citations: the endpoints that trigger it, the block settings, the stored-key header, and what makes a citation appear. Read when you want cited web research in a report, or a Perplexity or Parallel.ai block produces no citations."
---

# Automatic Citations from Web Search

When a **Web API Call** block calls **Perplexity** or **Parallel.ai**, the platform automatically turns the answer's sources into citations in your report — the same clickable, traceable citations you get from document analysis. You do not configure anything extra on the block; you just call the right endpoint.

This is the easiest way to get **cited web research** into a report without writing any glue.

## What you get

- Numbers in the answer (revenue, percentages, dates, amounts) are wrapped as clickable facts in the report.
- Clicking a fact shows the **source URL and the exact quoted snippet** that supports it.
- The sources travel with the value — if a later block reuses the number, the citation follows.

See [Reading a Report](../workflows/reading-a-report.md) for what citations look like to the end user.

## What triggers it

Citations are created automatically when **both** of these are true:

1. The block calls one of the recognised endpoints:
   - **Perplexity** — `POST https://api.perplexity.ai/chat/completions`
   - **Parallel.ai** — the task `…/result` endpoint at `https://api.parallel.ai/v1/tasks/runs/{id}/result`
2. The response actually contains sources (Perplexity `search_results`, Parallel.ai `basis` citations).

If you call any other URL, the block behaves like a normal Web API Call and produces no citations. If you hit a recognised endpoint but the response has no sources, the block still returns the raw text — just without citations.

## Setting up a Perplexity block

| Setting | Value |
|---------|-------|
| URL Template | `https://api.perplexity.ai/chat/completions` |
| HTTP Method | `POST` |
| Headers | Header Key `Authorization`, Header Value `Bearer {StoredKey.PerplexityApiKey}` |
| Request Payload Template (JSON) | the chat request (see below) |

```json
{
  "model": "sonar-pro",
  "messages": [
    { "role": "system", "content": "You are a credit research assistant. Be precise and factual. Always cite your sources." },
    { "role": "user", "content": "Provide an ownership and corporate structure analysis of {Counterparty.Name}. Cite all sources." }
  ]
}
```

`{StoredKey.PerplexityApiKey}` names your organization's [stored key](../../features/user-management/stored-keys.md) `PerplexityApiKey`; the older `{Organization.PerplexityApiKey}` names the same key. The request carries the key, and the run record keeps the placeholder, so you never paste a raw key into the block. `{Counterparty.Name}` and other [placeholders](block-reference.md#placeholder-system) are substituted before the call.

## Setting up a Parallel.ai block

Parallel.ai is a long-running ("async") task API: you submit a task, poll its status, then fetch the result. The Web API Call block does this natively with **poll-until-condition** mode — point it at the task-run URL, set the status path and success value, and set the completion URL to the `/result` endpoint. The citation extraction runs on that final `/result` body.

Authenticate with a header whose value names the stored key `ParallelApiKey`, for example Header Key `x-api-key` and Header Value `{StoredKey.ParallelApiKey}`, or the header Parallel.ai documents. See the [poll-until-condition settings](block-reference.md#web-api-call) for the full polling configuration.

## What makes a citation actually appear

Only **numerical values that are backed by a source** become clickable facts:

- **Ask the model to cite.** For Perplexity, a system prompt like "always cite your sources" makes it emit the `[1][2]` markers the platform reads. Without markers the platform falls back to matching each figure in the answer against the search-result snippets, so a figure keeps its link only when a snippet states it; a narrative claim with no figure gets no source.
- **Numbers must appear in the source.** A figure becomes a clickable citation only if the supporting snippet/excerpt actually contains that number (allowing for rounding and unit differences). A number the model stated but no source mentions is left un-cited.
- **Only numbers get the inline marker.** Narrative claims still record their sources internally, but the clickable `[i]` badge in the report appears on numerical facts.

## Tips

- **Prompt for specifics.** "What was the exact FY2024 revenue and segment figures? Cite sources." produces more citable numbers than an open-ended question.
- **Use the exact host.** Citations key off the endpoint host — a proxy or a different base URL turns the feature off silently.
- **Reuse downstream.** Pull the cited block into a later AI Query with its `{TagName}` placeholder; if the later block repeats a figure, the citation is traced back automatically.

## Done when

Run the block (or its section) and open the report: a figure from the answer shows the citation badge, and clicking it shows the source URL and the quoted snippet. A figure with no badge was not stated in any source the response returned.

## Related pages

- [Block Reference → Web API Call](block-reference.md#web-api-call) — every Web API Call setting, including polling
- [Reading a Report](../workflows/reading-a-report.md) — how citations appear to the reader
- [Web Search Citations (architecture)](../../architecture/citations/web-search-citations.md) — how it works under the hood
