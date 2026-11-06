---
type: reference
status: current
description: "The rules for a message that a person reads: an error, a refusal reason, a notice or a run log line. It names the readers, gives each rule with its published source, and shows messages before and after the rules. Read when you write or change any text that the apps or the API show to a user."
---

# Writing Messages for Users

A message tells a person that something did not go as they wanted, and what to do now. At that moment it is often the only help the person has. Write it for that person, not for the code that produced it.

## Who reads a message

Three people read the same text:

- An analyst in the Vue app, who uploads documents and reads reports.
- A workflow builder in the admin app, who edits workflows and reads run logs.
- A developer who calls the API.

The API returns the text in its error, and both apps show that text as it is. Write it in the words of the analyst. The machine-readable part goes in the error code, such as `File.Refused.FormatNotRead`, not in the text.

## The rules

1. **Say what happened, in the reader's terms.** Describe the file, the run or the block that the reader works with. Never name a build, a module, a project, a class, a setting key or an internal state. When the product itself is the subject, call it "the platform", as the other messages do.

2. **Name the exact thing.** Give a file type by its common name and its extension. Give a block by the label that the editor shows, and a request field by its name.

3. **Give the next step.** Give one step, the simplest one that works, as an instruction: "Save the workbook as .xlsx, then upload it again." When the reader can do nothing, say what the platform did instead. For example, say that the run went on without the block.

4. **Say only what is true and useful here, and mention the full edition only at its limit.** Give a cause only when it helps the reader take the step. When the reader meets something that only the paid edition does, add one sentence after the next step that says what that edition does: "The full edition reads email files directly and extracts their attachments." Say what it does, and never ask the reader to upgrade or to buy. Mention the edition nowhere else: not in a banner, a badge or an upload hint. The edition has no public name yet, so `ProductEditions.FULL_EDITION` holds the name "The full edition", and a rename happens there.

5. **Keep it short and level.** Use one or two sentences: the problem, then the step. Use no "please", no "sorry", no blame and no jokes.

6. **Use one name for one thing.** Use the name that the screen uses. If the editor calls a block "JSON to XLSX", the run log does too, not `Json2Xlsx`.

7. **Treat released text as a contract.** A test, a page of these docs or an API caller can depend on the exact text. Change the text, its tests and every page that quotes it in one change.

## Check a message

Read the message as the analyst who just saw it, and answer three questions:

1. Do I know which file, block or field it means?
2. Do I know what to do now, without asking anyone?
3. Is there a word in it that I would not know?

The message is ready when the first two answers are yes and the third is no.

## Examples

The first seven messages appear only in a build that does not read the format or does not include the block. Each of them first said "the open build", an internal name that no reader of the free platform knows, and none gave a next step or said what the full edition does. The last two appear in every build. They first described the file in the code's terms, and gave no next step.

| Where the reader sees it | Before | After |
|---|---|---|
| An upload of an `.eml` file | The open build does not read email messages (.eml). | The platform does not read email files (.eml). Save the email as a PDF, or upload its attachments instead. The full edition reads email files directly and extracts their attachments. |
| An upload of a `.msg` file | The open build does not read Outlook messages (.msg). | The platform does not read Outlook email files (.msg). Save the email as a PDF, or upload its attachments instead. The full edition reads Outlook email files directly and extracts their attachments. |
| An upload of an `.xls` file | The open build does not read Excel 97-2003 workbooks (.xls). | The platform does not read Excel 97-2003 workbooks (.xls). Save the workbook as .xlsx, then upload it again. The full edition reads Excel 97-2003 workbooks directly. |
| A run log, for an email that a run must open | The open build does not read email. | The platform does not read email files. Save the email as a PDF, then upload it with its attachments. The full edition reads email files directly and extracts their attachments. |
| The API answer to a confidential run | The open build does not anonymise documents, so it cannot start a confidential run. | The platform cannot anonymise documents, so it cannot start a confidential run. Start the run with isConfidential set to false. The full edition anonymises documents before any model reads them. |
| A run log, for a block the platform does not include | The open build does not include Json2Xlsx blocks, so this block did not run. | The platform does not include JSON to XLSX blocks, so this block did not run. Remove the block if the workflow does not need it. The full edition includes JSON to XLSX blocks. |
| The editor panel of that block | The open build does not include JSON to XLSX blocks, so this block cannot run here. | The platform does not include JSON to XLSX blocks, so this block cannot run. Its settings stay as they were saved. Remove the block if the workflow does not need it. The full edition includes JSON to XLSX blocks. |
| An upload of a PDF with nothing behind its header | The file starts like a PDF but carries no objects and no cross-reference table. | The PDF has no content and cannot be opened. Save or download it again, then upload the new copy. |
| An upload over the size limit | The file is 12582912 bytes; the limit is 10485760 bytes. | The file is 12 MB, more than the limit of 10 MB. Upload a smaller copy, or split the document into smaller files. |

## Sources

The rules come from published guidance. Each one names the rule that it supports.

- Nielsen Norman Group, [Error-Message Guidelines](https://www.nngroup.com/articles/error-message-guidelines/): human-readable language, a precise description, constructive advice, no blame, no humor (rules 1, 3 and 5).
- Microsoft, [Error messages (Windows design guidelines)](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-error): a message is relevant, actionable, user-centered ("in terms of target user actions or goals, not in terms of what the code is unhappy with"), brief, clear, specific and courteous, and gives no unlikely causes (rules 1 to 5).
- Google, [Writing helpful error messages](https://developers.google.com/tech-writing/error-messages/summary): identify the cause and the invalid value, explain how to fix it, aim at the audience, use terminology consistently (rules 2, 3 and 6).
- GOV.UK Design System, [File upload](https://design-system.service.gov.uk/components/file-upload/): a specific message for each error state of an upload, which names the file types (rule 2).
- Atlassian Design System, [Error messages](https://atlassian.design/foundations/content/designing-messages/error-messages): one or two sentences, what to do next, no made-up reason, no "please" or "sorry", no jargon (rules 3 to 5).
- GitLab, [Feature discovery moments (issue 333005)](https://gitlab.com/gitlab-org/gitlab/-/issues/333005): notices about paid features are "contextual, valuable, consistent and dismissable" and offered "in a non-intrusive way" (rule 4).
- WunderGraph, [How to align Open Source and Enterprise Sales](https://wundergraph.com/blog/open_source_and_enterprise_sales), and OpenMausBot, [issue 3134](https://github.com/milind-soni/OpenMausBot/issues/3134): self-hosting users push back on offers that they cannot use, so the mention stays at the limit (rule 4).
- GitLab Pajamas, [How to write UI text](https://design.gitlab.com/content/ui-text): no "sorry" or "please", existing product terms before new ones (rules 5 and 6).
