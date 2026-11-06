---
type: guide
status: current
description: "How to set a workflow's page overview prompts on the workflow editor's Document Classes tab: the default, a class's own prompt, the one-page version, and the warning for blocks that search pages a class never summarises. Read when a search step finds nothing in a document class, the Document Classes tab warns about page overviews, or you write an overview prompt."
---

# Page overview prompts

When a run reads a document, the platform can write a short overview of each page. Search steps find pages through these overviews: similarity search, the retrieval agent, a keyword search that searches overviews or also matches by similarity, the **Build Topics For All Documents** block, an AI Query block with **Use Page Overviews Instead of Full Text** on, and a Retrieve Data From Documents block with **Get Overviews** on. A page gets an overview only when the workflow has a prompt for it. Without one, the page gets no overview, and these steps find nothing in it.

Each workflow holds its own overview prompts. You set them on the **Document Classes** tab of the workflow editor. They travel with the workflow when you export it. [Overview generation](../architecture/rag/overview-generation.md) explains what the platform does with them.

## Where a page gets its prompt

A page takes the first prompt that has a user prompt:

1. The overview prompt of the page's document class.
2. The workflow's default overview prompt.

A page of a spreadsheet or a CSV file is read alone, without the pages around it. It takes the **one-page version** first: the class's one-page version, then the default's one-page version, then the order above.


A page with no prompt gets no overview. The platform makes no model call for it.

## Set the default prompt

1. Open the workflow in the editor and select the **Document Classes** tab.
2. In the **Page overviews** card, select **Edit**.
3. Write the system prompt and the user prompt. In the user prompt, `{0}` becomes the page number. To start from the platform's standard prompts, select **Use the standard prompts**.
4. To use the advanced model, turn on **Use the advanced model**. This switch alone does not rewrite overviews a run already wrote: they are written again only when the text of a prompt changes.
5. To give spreadsheets their own prompt, open **One-page version (spreadsheets, CSV)** and fill it in.
6. Select **Save**, then save the workflow.

The card shows **Prompt: Set** and **One-page version: Set** for the parts that have a user prompt. A system prompt alone counts as empty, and the editor says so under the user prompt. To stop all overviews for classes without their own prompt, clear both prompts and save.

## Give a class its own prompt

1. On the **Document Classes** tab, select the edit button of the class.
2. Under **Page overview prompt**, write the prompts as for the default.
3. Select **Save**, then save the workflow.

An empty class prompt means the class uses the default. A class for documents the workflow produces has no overview prompt: the platform does not write overviews of its own output.

The **Overview prompt** column of the class table shows where each class gets its prompt: **Own**, **Workflow default** or **None**.

## The warning on the Document Classes tab

The tab warns when a block searches pages that get no overview: for example, `1 block reads page overviews, but Presentation documents get none. Give each an overview prompt, or set a default.` Select **Show blocks** to see the blocks. A class in the warning shows a warning icon in the **Overview prompt** column.

To clear the warning, give each listed class its own prompt, or set a default. If the workflow has no document classes, the warning asks for a default: `1 block reads page overviews, but documents get none. Set a default overview prompt.` The warning checks the main prompt: a one-page version alone covers only spreadsheets and CSV files, so it does not clear the warning.

A template saved or checked through the API (`PUT` or `POST .../validate` on `/workflow-templates/{workflowTypeId}/content`) gets the same message in the response's `warnings`, on the `DocumentClasses` path. Like every template warning, it does not block the save.

## Done when

- The **Page overviews** card shows the default you want, or **Empty** on purpose.
- The **Overview prompt** column shows **Own** or **Workflow default** for every class that a search step reads.
- The Document Classes tab shows no warning.
