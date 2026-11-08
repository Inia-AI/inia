---
type: guide
status: current
description: "Walks an analyst through running a report in the Vue app: Create New Report, choosing the report type, uploading and classifying the documents, the company and parameters, the review, and following the run until its report opens. Read when you run a report for the first time, or a step of Create New Report asks for something you do not have."
---

# Run a Report in the Web App

A report is one run of a report type over the documents you give it. This page takes you from the dashboard to a finished report. [Reading a Report](reading-a-report.md) takes it from there: the statuses, the figures and their citations.

## Before you start

- Sign in to the Vue app. Your account needs a role that may run reports, or the dashboard shows no **Create New Report** button.
- Have the documents at hand, for example an annual report as a PDF.

On the dashboard, click **Create New Report**. The page lists four steps: **Report type**, **Documents**, **Details** and **Review**. **Continue** moves to the next step, and **Back** returns to the one before.

## 1. Choose the report type

Pick the kind of analysis you want. The next steps follow from it: the documents it needs, and the details it asks for.

## 2. Upload the documents

Add the source files. The step lists the document classes of the report type:

- **Required**: the report cannot run without a document of this class. Its name stays in the list until you upload and classify one.
- **Suggested** and **Optional**: the report runs without them.
- **· 1 only**: the report takes one document of this class. Remove the extras to continue.

Each uploaded file gets a class. **Let us decide** leaves it to the platform, which reads each document and decides what it is. Choose a class yourself only when you are sure. When the platform cannot decide for a class, the step says so, and you choose the class for those files.

## 3. Set the details

Set the company the report is about, its peer companies when the report type compares with peers, and any parameters the report type has. A parameter you leave alone keeps the default of the report type.

## 4. Review and submit

The review lists the report type, each document with its class, and each detail. A class the platform will choose reads **We decide**, and a parameter you left alone shows its value with **(template default)**. Click **Submit**.

The app says **Report generation started successfully** and returns to the dashboard.

## Follow the run

Open the **Reports** list. It shows the new run, newest first, and refreshes itself while the run moves through its statuses. When the run finishes, click its row to open the report. A failed run opens too: its page says what went wrong.

## Done when

- The **Reports** list shows the new run with your report type and company.
- When the run has finished, the report opens and its figures link to their pages, as [Reading a Report](reading-a-report.md) shows.

## Related pages

- [Reading a Report](reading-a-report.md): the statuses, the finished report and its citations.
- [Quickstart](../../getting-started/quickstart.md): the same steps with the sample report types, on the one-command stack.
- [Document Classes](document-classes.md): what a document class is and how a report type uses it.
