---
type: reference
status: current
description: "Lists, area by area, what the open build of the platform holds and what the full edition adds, and what the open build does when it meets a part of the full edition. Read when you need to know whether a feature, a block or a document format is in the open build."
---

# The open build and the full edition

This repository builds the open build of the platform. The full edition is the same platform with
modules that this repository does not hold. This page lists, area by area, what the open build holds
and what the full edition adds, and what the open build does when it meets a part of the full
edition.

Both builds use one database schema. A database moves from one build to the other and keeps its
data.

## Area by area

| Area | The open build | The full edition adds |
|---|---|---|
| Workflows | The workflow editor, version history of each workflow, and replay from an execution: a run reuses the web search, exchange rate and external API outputs of an earlier run instead of fetching them again. | Replay from a validation case. |
| Blocks | AI Query, Retrieve Data From Documents, Build Topics For All Documents, Apply Template To Data, Calculate Formula, Loop Start and Loop End, Intelligent Search, Exchange Rate, Web API Call, Dynamic Chart, Switch Case, Merge JSON, String Manipulation and AI Agent. Your own block types: [Add a block type](../guides/blocks/add-a-block-type.md). | The Json2Xlsx, Peer Financials and Verification Group blocks. |
| Documents | Uploads of PDF files, Word files (.docx), Excel workbooks (.xlsx and .xlsm), images, plain text and CSV. Reports export as Word files and as Markdown. | Uploads of email files (.eml and .msg), with their attachments, and of Excel 97-2003 workbooks (.xls), through the Aspose document implementation. |
| Signatures | All of Signatures: signature fields, the proposals and votes that set an organization's values, their audit log, and a test of a changed value on a block of an earlier run. | Nothing more. |
| Agents | The AI Agent block on the agent runtime, with a plain Anthropic API key. | Nothing more. |
| Models | Claude models through the Anthropic API, Amazon Bedrock or Azure AI Foundry, and Cohere embeddings. | Nothing more. |
| Validation and regression | None. | Validation of reports against expected cases, on demand or on a schedule, and regression runs. |
| Document intake screening | None. | Screening of each upload before a run reads it. |
| Confidential runs | None. | Anonymisation of the documents of a confidential run before any model reads them. |
| Industry modules | None. | Hedge fund monitoring, private equity monitoring, and the Financial vertical. |
| Templates | The sample workflows in `platform/samples/`. | The library of tuned workflow templates. |

## What the open build does at each edge

- **A Json2Xlsx, Peer Financials or Verification Group block.** A workflow keeps the block with its
  settings. The editor shows that the platform does not include the block type. A run fails the
  block with a message that says so and names the full edition.
- **An email file or an Excel 97-2003 workbook.** The upload is refused with a message that says how
  to convert the file, for example to save an email as a PDF.
- **A confidential run.** The platform refuses to start it, with a message that says it cannot
  anonymise documents. Start the run as not confidential.
