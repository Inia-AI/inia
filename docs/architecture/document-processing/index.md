---
type: explanation
status: current
description: "The file-format interface the platform reads and writes documents through, its free implementation in the open build, the PDFium worker process, the format handlers (PDF, Word, Excel, plain text, images, email), the page-image DPI budget, and the password unlock gate. Read when you change how a document format is read or written, or debug a document that fails to page, render or unlock."
---

# Document processing: the file-format interface

**On this page:** Library map · Fonts: the metric twins · The file-format interface · Format handlers · Email: `.msg` and `.eml` · Password-protected files: the unlock gate · Related pages

The platform reads and writes documents through the file-format interface in `IniaAi.FileFormats`. The open build's implementation is free: **PDFium** for PDF files, in a worker process, **ClosedXML** for workbooks, **Open XML SDK** for Word files, and our own code on top. **SkiaSharp** does the platform's own picture work: reading an image's size, and scaling and compressing page images.


This page documents which library handles which format, each format's handler, and the password-unlock gate for encrypted PDF, Excel and Word uploads.

## Library map

`platform/IniaAi.FileFormats/IniaAi.FileFormats.csproj`, the open project, references no Aspose package:

| Library | Licence | Handles |
|---------|---------|---------|
| `OpenMcdf` | MPL-2.0 | The compound-file container of an encrypted Office file and of a legacy workbook, read at intake |
| `FileSignatures` | MIT | Content-type detection at intake |
| `SkiaSharp` (+ per-OS native assets) | MIT | Image size checks, scaling, and JPEG/PNG encoding of page images |
| `ClosedXML` | MIT | Workbook reading, template filling and the workbook side of the Word copy |
| `ExcelNumberFormat` | MIT | Excel number formats, under our own `ExcelNumberText` |
| `DocumentFormat.OpenXml` | MIT | Word reading, template filling, the Word copy of a workbook and the Word report export |
| `HtmlToOpenXml.dll` | MIT | Report HTML into Word paragraphs and tables, for the Word report export |

`IniaAi.FileFormats.PdfiumWorker`, the PDF worker process, references the **bblanchon.PDFium** packages (Apache-2.0) and SkiaSharp. `IniaAi.FileFormats` references the worker project so that every host carries it, but calls none of its code: see [The PDFium worker process](#the-pdfium-worker-process).

SkiaSharp's native assets are referenced explicitly for Linux, macOS, and Win32 in the open project so the raster backend resolves on every target OS:

```xml
<PackageReference Include="SkiaSharp" />
<PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" />
<PackageReference Include="SkiaSharp.NativeAssets.macOS" />
<PackageReference Include="SkiaSharp.NativeAssets.Win32" />
```

The open build has no Aspose package and no private feed: it restores from nuget.org alone. OCR itself needs no package here — it is model-based (see [OCR architecture](../ocr/index.md)).


## Fonts: the metric twins

The deployed images carry no Microsoft core font: their licence forbids shipping them inside a product. They carry the free metric twins instead — Liberation Sans, Serif and Mono for Arial, Times New Roman and Courier New, Carlito for Calibri, Caladea for Cambria — which keep every glyph's width. fontconfig maps the names.


## The file-format interface

Open code reads and writes documents through these interfaces in `IniaAi.FileFormats`. It names no Aspose type.

| Interface | What it does | Open build |
|---|---|---|
| `IPdfFormat` | Opens a PDF as an `IPdfDocument`: the page count, each page's box, the text of a page or of an area of it, the boxes of its text fragments, and a page or an area of it rendered to PNG or JPEG at a given resolution | `PdfiumPdfFormat` |
| `IWorkbookFormat` | Opens a workbook as an `IWorkbookDocument`: every sheet in workbook order, hidden ones included, with each cell's text as Excel shows it | `ClosedXmlWorkbookFormat` |
| `IEditableWorkbookFormat` | Opens a workbook template to fill, calculates its formulas and saves it in its own format | `ClosedXmlEditableWorkbookFormat` |
| `IDocumentUnlocker` | Removes the password protection of an uploaded PDF, workbook or Word document | `OpenDocumentUnlocker` |
| `IWordPageReader` | Reads a `.docx` page by page. A page is the reader's own split of the text, the same on every call, so no caller relies on a page number | `OpenXmlWordPageReader` |
| `IWordTemplateFormat` | Opens a Word template to fill with values and Markdown | `OpenXmlWordTemplateFormat` |
| `IWorkbookToWordConverter` | Writes the Word copy of a filled workbook | `OpenXmlWorkbookToWordConverter` |
| `IReportFileWriter` | Writes a run's report as a file in one export format; a build exports the formats it registers a writer for, and a writer registered later replaces an earlier one of the same format | `MarkdownReportWriter` (`.md`), `OpenXmlWordReportWriter` (`.docx`) |
| `IEmailToPdfConverter` | Converts an email to a PDF of its body, and an Outlook message to an RFC 822 one | none |
| `IReadableFormats` | Names the formats the build reads; intake refuses the others | `OpenBuildFormats`: refuses `.eml`, `.msg` and `.xls` |

`AddOpenFileFormats()` (`IniaAi.FileFormats`) registers the free implementation. The WebApi and WebApp hosts and the test service provider call it. Every open caller gets its instance from dependency injection. The callers are document processing, OCR, page images, the report export, the intake gate and email expansion.



The rules on top of a format stay in open code and work through the interface. `PdfExtractor` uses the page box and the text fragment boxes to find a scan of two facing pages. `PdfBox.Half` gives the area of each half, for its text and for its page image. `ExcelUtils` and `ExcelRange` build sheet Markdown from cell text.

`IPdfFormat.Open` and `IWorkbookFormat.Open` take over the stream: the document disposes it. They treat every file the same way. A PDF that lost its `startxref` pointer opens after the platform's own cross-reference rebuild (`PdfUtils.TryRepairStartxref`). `IPdfDocument.RepairedBytes` then hands the rebuilt file to the caller to store. A file that still needs its password fails with `DocumentPasswordProtectedException`, which names the document. Model page images open a PDF with `rewriteBeforeRendering`. Some scanned PDFs render in Aspose only after a save and a reopen; PDFium renders them without it.

### A build without the paid project

A build without `IniaAi.FileFormats.Aspose` reads PDF files, `.xlsx` and `.xlsm` workbooks and Word files, unlocks them, fills Word and workbook templates, and exports a report as Word and as Markdown, all on the free implementation. It reads no email and no Excel 97-2003 workbook: `OpenBuildFormats` makes the intake gate refuse `.eml`, `.msg` and `.xls` with the reason `FormatNotRead`, also for an upload that its sender vouches for, such as an editor's upload in the workflow editor. The API refuses a confidential run, because nothing anonymises its documents.

[Replace a File-Format Implementation](file-formats.md) says how to put another implementation in place of one format or of all of them.


### The PDFium worker process

PDFium is native code, and a crafted file can crash or hang it. The server never loads it. `PdfiumPdfFormat` starts one `IniaAi.FileFormats.PdfiumWorker` process per open PDF file, on the server's own runtime and dependency files, with an empty environment, so the worker inherits none of the server's settings. Requests to one worker go one at a time, so the pages of one document render one after another. On Linux the worker raises its own out-of-memory score, so the kernel kills it before the server. The two exchange length-prefixed messages over the worker's standard input and output (`PdfiumWorkerProtocol`): open, page box, text, text fragment boxes, render, and unlock. A worker that gives no answer within two minutes is stopped. A worker that stops fails only its own document, with a `PdfiumWorkerException`, and the next document starts a new worker.

[`PlatformSettings:PdfReader:MaxWorkers`](../../operations/application-configuration.md#pdfreader) limits how many workers one server process runs at once (`PdfiumWorkerLimit`, shared by `PdfiumPdfFormat` and `OpenDocumentUnlocker`). Each open document and each PDF unlock holds one worker until it ends, and a document past the limit waits for a free worker. A flow holds one document of a file at a time: `DocumentProcessingService` closes its copy before OCR opens one, and `OcrTranscriptionService` closes its copy when its last page renders.

On Linux the worker sandboxes itself before it reads a byte of the file (`WorkerSandbox`). It stops being dumpable; when it starts as root, as the image starts it, it switches to `pdf-reader`, a user that owns no file, so it cannot read the server's secrets or its private folders; and a seccomp filter refuses every new socket and io_uring, the calls that reach another process, the kernel keyring and every new process (`SeccompFilter`). It starts as root rather than as `pdf-reader` because a process that starts as that user is dumpable until it turns that off, and another worker could open its memory then. A worker that cannot set a limit fails its document. [Replace a File-Format Implementation](file-formats.md#pdfium-runs-in-a-worker-process) says what the sandbox covers, what a mounted file keeps, and how to check it in a running container.

Tests get their implementation from `AddFileFormatsUnderTest()` in `IniaAi.Testing.Documents`, the one registration every test uses, which registers the build's formats as the hosts do. The contract tests in `IniaAi.FileFormats.ContractTests` pin what the pipeline needs from any implementation: one abstract class per interface, with small invented fixtures. An implementation passes when a subclass that returns it runs green. `WordReportWriterContract` writes the reports the export writes, through `WorkflowDocumentAdapter`, and checks the content and the look of the Word file. [Replace a File-Format Implementation](file-formats.md) says how to put another implementation in place of one format or of all of them. Tests build their Word files with `WordFixtures` on Open XML SDK and read them with `WordFile` from `IniaAi.Testing.Ooxml`, a project that references no document library.


## Format handlers

### PDF: `platform/IniaAi.FileFormats/Pdf/`

| Type | Project | Responsibility |
|------|---------|----------------|
| `IPdfFormat`, `IPdfDocument`, `PdfBox` | open | The PDF interface, described above |
| `PdfExtractor` | open | Text through `IPdfDocument`: `ExtractTextSkippingFailedPages`, which skips a page that fails and stops at a character budget, and `ExtractPaginatedText`, which reads a scan of two facing pages as two halves |
| `PdfUtils` | open | Byte-level checks and the `startxref` rebuild; it uses no PDF library |
| `PdfiumPdfFormat`, `PdfiumPdfDocument`, `PdfiumWorker` | open | The free implementation: each call is one request to the document's worker process. Text is laid out in rows as Aspose's pure text mode lays it out, and text outside the page's crop box stays out |


These page images are what the OCR layer consumes downstream. The standard page-image path (`platform/IniaAi.Core/Documents/PagesHelper.cs`) renders PNG at a **per-page DPI budget**: `min(100, 1568 × 72 / longest page edge in pt)` (`ModelImageLimits.ResolveRenderDpi` with `VisionTier.Standard`), so the rendered long edge never exceeds the standard tier's 1568px — A4/letter pages stay at 100 DPI, wider pages render proportionally lower (A3-landscape at 94 DPI). The OCR path (`platform/IniaAi.Core/Services/OcrTranscriptionService.cs`) renders its own JPEG at the long edge of the model that transcribes it, which reaches 2576px for current models — see [OCR architecture](../ocr/index.md#pipeline). This path keeps the standard tier because AI query requests carry up to 100 page images, and Anthropic holds every image of a request with more than 20 images to 2000px. A page renders once per job: the image stays on the page for every later block, and parallel AI Query blocks that ask for the same document's pages at once wait on one lock per document, so the first renders them and the rest reuse its images.

### Word: `platform/IniaAi.FileFormats/Word/`

The open project holds `IWordPageReader`, `IReportFileWriter`, `WorkflowDocumentAdapter`, which turns a run's report into the element tree that every report writer writes, `ContentSegmenter`, which splits report content into text, tables and images, and `ChartDataTable`, a chart's data as rows of text.

#### The Markdown report writer

The report's Markdown export is open code in `platform/IniaAi.FileFormats/Markdown/`. It uses no Aspose type and no extra package, so a build without the paid project can export a report. `MarkdownReportWriter` reads the same `IDocumentElement` tree as the Word writer and turns each paragraph's HTML into Markdown in one pass with its own forgiving HTML reader (`HtmlFragment`, `HtmlToMarkdown`). What the file holds: [Markdown export](../../reference/api/reports.md#markdown-export-md).


In the open project (`platform/IniaAi.FileFormats/Word/OpenXml/`), `OpenXmlWordReportWriter` writes the same report with Open XML SDK: `HtmlToOpenXml` turns each paragraph's HTML into Word paragraphs and tables, a chart goes in as the picture that `IChartImageRenderService` draws or, without one, as a table of its data, and a grid row that pairs a chart with text floats the chart beside the text. `WordReportWriterContract` holds both writers to the same content, look and schema.

**Ingestion in the open build.** `OpenXmlWordPageReader` reads a `.docx` or `.docm` with Open XML SDK and splits it into pages of `IniaAi:WordCountPerPage` words (250 by default), since it has no layout engine. Tracked changes read as accepted, a field shows its result, list labels read as Word shows them, a text box follows its paragraph, and each section's header and footer lines close the page of the section's first block. A PNG or JPEG picture that fits the model's vision tier passes as it is, and any other picture becomes a PNG that fits it; a Windows metafile (EMF) picture stays out, because no free image library decodes it.


**Embedded image regions OCR like scanned pages.** `CreateWordPagesAsync` sends each image region the page reader returns through the same one-shot model transcription scanned PDF pages use (`OcrTranscriptionService.ProcessImageAsync`) and appends the transcriptions after the page's native text — `Page.Text` carries the merged result, `RawText` keeps the native text alone, and the page becomes `OCRSuccessful` when at least one transcription merges. A failed region transcription marks the page `OCRFailed` with its text dropped, so the next processing run re-extracts and retries just that page. A region whose transcription comes back empty is an image with nothing to transcribe — not a failure — and a region the OCR pass declines (the model's content filter today, the fidelity judge when re-enabled) is skipped with a warning; in both cases the page keeps its native text and stays `ProcessedSuccessfully` when no other region merged (Word pages are never routed by image — unlike PDF pages, there is no downstream re-render path for them). A region that cannot be materialized at all logs a warning and is dropped.

One current limit: legacy binary `.doc` stays produce-only — upload validation accepts `.docx` only.

### Excel: `platform/IniaAi.FileFormats/Excel/`

Open code reads a workbook through `IWorkbookFormat`: `ExcelUtils` and `ExcelRange` build sheet Markdown from `IWorkbookSheet.CellText`, and `ExcelAnalysisConfigSheet.TryRead` reads the analysis config sheet that a block stamps. A workbook is written through `IEditableWorkbookFormat`, `IEditableWorkbook` and `IEditableSheet` (`platform/IniaAi.FileFormats/Excel/`): the writer opens a template, writes values and bold runs, calculates the formulas with a time limit (`FormulaCalculation` names the cells an engine could not calculate), measures wrapped text through `IWrappedTextMeasurement`, and saves in the template's own format. `ExcelAnalysisConfigSheet.Stamp` stamps the analysis config sheet through the same interface. In the open project, ClosedXML implements both: `ClosedXmlWorkbookFormat` and `ClosedXmlEditableWorkbookFormat`. `ExcelNumberText` shows a number as Excel does, on top of ExcelNumberFormat: it picks the format section by the sign, rounds a halfway value away from zero, fits General into 11 characters, and shows Excel's built-in short date in the reader's culture. A workbook that ClosedXML cannot load because of its comments, pictures or a people part the package lacks loads again from a copy without them (`WorkbookPackage`). `.xls` is paid-only. The contracts of the edit interfaces are `EditableWorkbookContract`, `WordTemplateContract` and `WorkbookToWordContract` in `IniaAi.FileFormats.ContractTests`.



### Plain text and markdown: no handler

`.txt`, `.md` and `.markdown` need no format library: `DocumentProcessingService.CreateTextPagesAsync` reads the stored bytes as UTF-8 and makes the whole text page 1 as written (`Text` and `RawText` alike, `ProcessedSuccessfully`); overview generation and embeddings then run on it like any other page. This is the shape a pasted baseline takes when it arrives through [`POST /documents/text`](../../reference/api/documents.md#create-text-document) — clean text becomes pages directly, with nothing extracted, converted or rasterized in between. `MediaType.TextMarkdown` (`text/markdown`) was added for `.md` and `.markdown`; `.txt` reads as `TextPlain`.

### Images: `platform/IniaAi.FileFormats/Images/`

`Pictures` holds the platform's picture work on **SkiaSharp** (not Aspose): it reads an image's size from its header without decoding pixels (the upload check and screening use this), and scales a picture to a PNG within the model's edge limit (image document pages use this). `ImageCompressor` uses the same helpers to resize and JPEG-compress images toward a target byte size, keeping page images within the size limits the model/transport layers expect.

SkiaSharp reads PNG, JPEG, GIF, WebP and BMP. It does not read TIFF, so a stored TIFF document gets no page picture. Uploads admit only JPEG and PNG images, so no new TIFF can arrive. The platform used SixLabors.ImageSharp until October 2026: its 3.x line stopped getting security fixes, and 4.x needs a Six Labors license key at build time, which an open-source build cannot carry.


## Email: `.msg` and `.eml`

The open build reads no email: it registers no `IEmailToPdfConverter`, and its `OpenBuildFormats` makes the intake gate refuse `.eml` and `.msg` files with the reason `FormatNotRead` ([A build without the paid project](#a-build-without-the-paid-project)).


## Password-protected files: the unlock gate

`platform/IniaAi.FileFormats/Unlock/` is the ingest-time gate for password-protected PDF, Excel, and Word files. The open build's `OpenDocumentUnlocker` removes a PDF's security with PDFium in a worker process, saving a copy without its security handler, and decrypts an Office package with our own MS-OFFCRYPTO code (`OfficePackageDecryption`, agile and standard AES encryption); it gives a legacy workbook `UnsupportedProtection`. Intake never asks it: the gate refuses a format the build does not read, `.xls` included, before it tries to unlock the file. The open unlocker also refuses a spin count over the 10,000,000 that [MS-OFFCRYPTO] 2.3.4.10 allows, and a package that declares more bytes than it holds, both as `UnsupportedProtection`. `IDocumentUnlocker.TryUnlockAsync` decrypts once with a caller-supplied transient password and returns a stripped (unencrypted) working copy. The container to unlock comes from the bytes, never from the file name: `EncryptedContainerProbe` names it `Pdf` (an `/Encrypt` key in the file), `OfficePackage` (a compound file with an `EncryptedPackage` stream, which hides whether it holds a workbook or a Word document until decrypted) or `LegacyWorkbook` (a `FILEPASS` record in the `Workbook` stream). The [file content inspector](file-content-inspector.md) answers the same question, and a caller that already holds its `Encrypted` verdict passes the container in. `PdfiumUnlock` in the worker is the only code that touches PDFium's password APIs, and `EncryptedContainerProbe` reads the compound-file directory through OpenMcdf. The design is **decrypt-at-ingest**: the decrypted copy is what gets stored and processed, the encrypted original is discarded, and **the password is never persisted or logged** — it exists only for the duration of the upload request.


`TryUnlockAsync` returns a `FileUnlockResult` whose `FileProtectionStatus` drives every caller:

| Status | Meaning | Caller behavior |
|---|---|---|
| `NotEncrypted` | No protection detected (also returned for bytes that are not an encrypted container, and for a PDF whose content merely contains the literal `/Encrypt`) | Proceed with the original bytes |
| `Decrypted` | Protection removed; `DecryptedBytes` holds the stripped copy | Store/process the decrypted copy, set `Document.WasPasswordProtected`, write the audit row |
| `PasswordRequired` | Encrypted and no password was supplied | Prompt (interactive channels) or reject/skip |
| `PasswordIncorrect` | The supplied password does not open the file | Re-prompt or reject |
| `UnsupportedProtection` | Certificate- or DRM-based protection (e.g. AIP/LiveCycle) that no password can remove | Clear reject — no retry can succeed |

Behavioral rules baked into the gate:

- **Owner-only PDFs** (restrictions but no open password) are stripped silently — no prompt, `WasPasswordProtected` still set.
- **Signed + encrypted PDFs are decrypted anyway**: stripping encryption invalidates an embedded digital signature, so the result carries `ContainedSignature = true` and the upload response discloses the invalidation as a warning. The platform performs no signature verification internally, so nothing functional breaks.
- **A rights-managed Office package** is `UnsupportedProtection` with or without a password. Its key comes from a rights-management licence, not a password: it has an `EncryptedPackage` stream and no `EncryptionInfo` stream ([MS-OFFCRYPTO] 2.2.10). `EncryptedContainerProbe.IsRightsManagedPackage` reads that from the compound-file directory, so the gate never asks for a password that cannot work.
- Decrypted output is **re-opened and validated** before being accepted; a copy that fails re-open is treated as `UnsupportedProtection`.


**Where the gate runs** — every ingestion point calls it before bytes are stored. Three of them reach it through `DocumentIntakeGate` in `platform/IniaAi.Application/Documents/`, which runs the [inspector](file-content-inspector.md) first, hands only an `Encrypted` verdict to unlock, inspects the decrypted copy again, and writes the refusal record itself:

- API uploads (`POST /files` and `/workflow-executions/submit`) via `UploadFileCommandHandler` — see the [Files API](../../reference/api/files.md) for the `password` parameter and error codes.
- The Blazor upload card (`DocumentUploadCard.razor`) and the Vue SPA uploader, each with a password prompt dialog (retry on wrong password, per-file skip). The card names the reason when a file is refused and uploads nothing. On the workflow editor's pages the card's mouth is **vouched for** (the card's `ContentIsVouchedFor` parameter, set only there and only for a user in the `WorkflowEditor` or `SuperAdmin` role, becomes `IntakeMouth.ContentIsVouchedFor`): the people who build the workflows deliberately test bad documents, so only the two refusals that protect the platform stand there — a file over the upload limit and an archive that expands past the limits (`IntakeRefusalReasons.ProtectsThePlatform`) — and, in the open build, the refusal of a format it does not read, which the gate records without asking who vouches. Any other refusal admits the file, as the type its bytes are or, when they name none, as the type its name claims, and lets it reach the workflow. Uploads in the analyst app go through `POST /files`, which keeps the full check.
- Email attachment intake inside a run (`EmailIntakeJobStage`): a refused attachment is skipped with its reason and its record joins the run's trail; a refused body stops the email like a failed render.
- Email intake — **non-interactive** channels that can never supply a password: [expansion on `/submit`](../../reference/api/submit-workflow-execution.md#email-expansion) and the in-execution intake stage (`EmailIntakeJobStage`, which shares `EmailExpansionService` with `/submit`). In both, `NonInteractiveUnlockPolicy.GetSkipReason` maps `PasswordRequired`/`PasswordIncorrect`/`UnsupportedProtection` to a skip: the part is excluded and the reason is persisted as a `Skipped` row (section name `"Document intake"`) in `WorkflowExecutionLogs` on the execution — `/submit` additionally reports it in the response's `intakeSkippedFiles` array. Owner-only protection (openable without a password) is not a skip: the gate strips it and the channel stores the decrypted copy.



**Audit trail**: every successful unlock writes a `DocumentUnlockAuditLogs` row (organization, document id/name, actor — the uploading or run-starting user where one exists, `null` where no user started the intake — `PasswordSupplied`, `ContainedSignature`). The row carries no foreign key so it survives document deletion. See the [UserData database reference](../../reference/database/user-data-database.md).

**Defense-in-depth at the processing sites**: if an encrypted document ever reaches the page-extraction pipeline anyway (a detection false-negative, or a document ingested before the gate existed), `IPdfFormat`, `IWorkbookFormat` and `IWordPageReader` fail it with a `DocumentPasswordProtectedException` naming the document. The free implementation checks for the encrypted compound file or PDFium's password error before it reads. The job still fails (per-document isolation is a pre-existing gap), but `JobExecutor.HandleJobError` routes the actionable reason to a durable `Failed` `"Document intake"` row on the execution via `WorkflowOutputService.AppendDocumentIntakeErrorLogAsync` — instead of only the in-memory job record.


Out of scope in v1: password-protected PowerPoint, *write-protection* locks on sheets, workbooks, or Word documents (an editable-lock, not encryption — such files open fine), and retaining the encrypted original for byte-exact serve-back.

## Related pages

- [File content inspection](file-content-inspector.md) — the byte-level inspector that decides admission from what a file is, reports an encrypted container for this gate to unlock, and refuses structural bombs before expansion.
- [OCR architecture](../ocr/index.md) — how the rasterized page images produced here are turned into text.
- [Chart rendering](../chart-rendering/index.md) — the Chromium image path, with the ECharts bundle, used when embedding charts in Word output.


