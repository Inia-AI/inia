# Overview

The platform turns a set of documents into a written report. An analyst designs the report once, as
a workflow. After that, a user uploads the documents of a new case, the platform runs the workflow
on them, and the user gets a draft report to review.

## Who it is for

The platform is for teams that read many documents to write the same kind of report again and
again. An example is a credit review of a company, written from its annual report and its accounts.
The analyst puts the team's method into the workflow once, and every run follows it.

## What a run produces

- **A draft report.** Each section of the workflow becomes a section of the report. A reader opens
  it in the Vue app, edits it and confirms it.
- **Figures that link to the page that states them.** A model-call block asks for citations when
  its setting **Get Citations From Model** is on. The citations come from Claude models. In the
  report, click a figure to see its source document and page, and open an image of the page.
- **A log of each block.** It shows the block's inputs, the prompt it sent, and the answer.
- **Files.** A reader exports the report as a Word file or as Markdown.

## The words these pages use

| Word | What it means |
|---|---|
| Workflow | The design of one kind of report: its sections, the blocks in each section, its variables and the classes of document it reads. The Vue app calls it a report type. |
| Block | One step of a workflow. For example, an AI Query block asks a model about the documents, and a Web API Call block calls another system. A block can use the output of earlier blocks. |
| Document class | A kind of document that a workflow reads, such as an annual report. A user tags each upload with its class. |
| Run | One execution of a workflow on a set of documents. The API calls it a workflow execution. |
| Report | What a run writes: the draft that a reader reviews and confirms. |
| Citation | A link from a statement in the report to the page of the document that supports it. |
| Signature field | A named value that the prompts of a workflow use. Each organization can set its own value, through a proposal that other users approve. |

## The parts

Users start runs and read reports in the Vue app. Authors edit workflows in the WebApp, the admin
app. The API serves the Vue app and runs the workflows, and the agent runtime runs the AI Agent
block. One PostgreSQL or SQL Server database holds the data of every organization.
[Architecture](architecture.md) says what each part does and how they work together.

## The model accounts it needs

- **An Anthropic API key.** Every model call goes to a Claude model with this key. The platform
  needs no other model account.
- **A Cohere API key, optional.** It gives each page an embedding. The filter rules that pick pages
  by meaning need these embeddings. Without the key, the other filter rules still work, and a block
  whose rule needs embeddings says so.
- **An Exa API key and an ExchangeRate-API key, optional.** They serve the web search block and the
  Exchange Rate block.

An install on AWS or Azure can call the same models through Amazon Bedrock or Azure AI Foundry
instead, with the credentials of the host.
