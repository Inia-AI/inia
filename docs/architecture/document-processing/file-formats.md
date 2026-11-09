---
type: guide
status: current
description: "Names the document interfaces of the platform and their contracts, the free implementation the open build registers, how the PDFium worker process sandboxes itself, and how to put your own implementation in place of one format or of all of them and check it. Read when you replace or add a document format implementation, or check the PDF worker's sandbox."
---

# Replace a File-Format Implementation

**On this page:** The interfaces and their contracts · The open implementation · Register your implementation · Check it · What the Word contracts ask · Test fixtures · Done when

The platform reads and writes documents only through the interfaces in `IniaAi.FileFormats`. Any library can serve
them: the open build registers a free implementation, and the paid build registers the Aspose implementation after
it. This page names the free implementation, says how to put your own in place of ours, for one format or for all of
them, and how to check that it is right.

## The interfaces and their contracts

Each interface has an abstract contract class in `IniaAi.FileFormats.ContractTests`. A contract asserts only what the
interface's doc comments and its callers rely on, never what one library happens to write.

| Format | Interface | Contract |
|---|---|---|
| PDF | `IPdfFormat` | `PdfFormatContract` |
| Excel, read | `IWorkbookFormat` | `WorkbookFormatContract` |
| Excel, fill a template | `IEditableWorkbookFormat` | `EditableWorkbookContract` |
| Password removal | `IDocumentUnlocker` | `DocumentUnlockerContract` |
| Email | `IEmailToPdfConverter` | `EmailToPdfConverterContract` |
| Word, read | `IWordPageReader` | `WordPageReaderContract` |
| Word, fill a template | `IWordTemplateFormat` | `WordTemplateContract` |
| Word copy of a workbook | `IWorkbookToWordConverter` | `WorkbookToWordContract` |
| Report export | `IReportFileWriter` | `ReportFileWriterContract`; a Word writer runs `WordReportWriterContract`, which holds it |

## The open implementation

`AddOpenFileFormats()` registers these. Every library is free for commercial use.

| Format | Class | Library (licence) |
|---|---|---|
| PDF | `PdfiumPdfFormat` | PDFium, from the bblanchon.PDFium packages (Apache-2.0), in a worker process |
| Excel, read | `ClosedXmlWorkbookFormat` | ClosedXML (MIT) and ExcelNumberFormat (MIT), with our own number formatting on top |
| Excel, fill a template | `ClosedXmlEditableWorkbookFormat` | ClosedXML (MIT) |
| Password removal | `OpenDocumentUnlocker` | PDFium for PDF files; our own MS-OFFCRYPTO decryption on OpenMcdf (MPL-2.0) for `.xlsx`, `.xlsm`, `.docx` and `.docm` |
| Word, read | `OpenXmlWordPageReader` | Open XML SDK (MIT) |
| Word, fill a template | `OpenXmlWordTemplateFormat` | Open XML SDK (MIT) |
| Word copy of a workbook | `OpenXmlWorkbookToWordConverter` | Open XML SDK (MIT) and ClosedXML (MIT) |
| Report export | `MarkdownReportWriter`, `OpenXmlWordReportWriter` | Markdig (BSD-2-Clause); Open XML SDK (MIT) and HtmlToOpenXml (MIT) |
| Formats the build reads | `OpenBuildFormats` | none: intake refuses `.eml`, `.msg` and `.xls` |

The open build has no `IEmailToPdfConverter`. Intake refuses an email or an Excel 97-2003 workbook with the reason
`FormatNotRead`, also for an upload that the sender vouches for, because nothing after intake could read it.

### PDFium runs in a worker process

PDFium is native code, and a crafted file can crash or hang it. The server never loads it. Each open PDF file gets its
own process, `IniaAi.FileFormats.PdfiumWorker`, which the server starts with its own runtime and dependency files and
an empty environment, so the worker inherits none of the server's settings. The two talk over the worker's standard
input and output (`PdfiumWorkerProtocol`), one request at a time, so the pages of one document render one after
another. When the worker gives no answer within two minutes, the server stops it. When it stops, only its document
fails, with a `PdfiumWorkerException`, and the next document starts a new worker. On Linux the worker raises its own
out-of-memory score, so the kernel kills it before the server.

`PlatformSettings:PdfReader:MaxWorkers` limits how many workers one server process runs at once, 8 by default. Each
open document and each PDF unlock holds one worker until it ends, and a document past the limit waits for a free
worker. The wait blocks its thread, because opening a document is synchronous. No run has measured the default yet.
A flow holds one document of a file at a time, so a limit of 1 cannot make a flow wait for itself: page extraction
closes its copy before OCR opens one, and OCR closes its copy when its last page renders. On a long document the
rendering waits for the model, so the worker lives until near the end of the model calls.

On Linux the worker sandboxes itself before it reads a byte of the file, with no extra container privilege:

- It stops being dumpable, so no other process of its user can read or write its memory.
- When it starts as root, as the app's image starts it, it switches to `pdf-reader`, a user that the image creates and
  that owns no file. It starts as root because a process that starts as `pdf-reader` stays dumpable until this point,
  and another worker could open its memory then and keep the handle. The switch needs the SETUID and SETGID
  capabilities, which Docker gives by default. A worker that starts as root on a host without `pdf-reader` stops, and
  its document fails. A server that runs as another user starts the worker as that user, which switches nothing.
- It installs a seccomp filter that refuses every new socket and io_uring, so it reaches no network; the calls that
  read or write another process; the kernel keyring; and every new process, so nothing it starts outlives it.
- It starts with the .NET diagnostics off.

A worker that cannot set one of these limits stops, and its document fails; a PDF unlock then reports the file's
protection as unsupported. As `pdf-reader`, the worker reads the app folder and the files that every user can read.
It cannot read the server's secret files or its environment, the image's logs folder, the folders of the downloads of
an email's links, or the browser's folders while the browser runs as its own user. A file or folder that a deployment
mounts, such as a logs folder or a certificate, keeps the mode its host gives it.

To check the sandbox in a running container, start the worker in its check mode as the app starts it. It tries to
connect to a port on the loopback address, to read a file and to start a process, and prints what it reached:

```bash
docker compose exec api dotnet exec \
  --runtimeconfig IniaAi.WebApi.runtimeconfig.json --depsfile IniaAi.WebApi.deps.json \
  IniaAi.FileFormats.PdfiumWorker.dll --check-sandbox 80 /run/settings/Seeding__SuperAdminPassword
```

It says `started: root` and `runs as: pdf-reader`, and `network`, `file` and `process` each say `blocked`. On macOS
and Windows the worker runs without the sandbox.

## Register your implementation

The hosts register the open implementations with `AddOpenFileFormats()`. The WebApi calls it in
`platform/IniaAi.WebApi/Configuration/ApplicationServicesConfiguration.cs`, and the admin app in
`platform/IniaAi.WebApp/ServicesConfigurator.cs`.


- To replace every format, write your own registration method, such as `AddMyFileFormats()`. Call it in place of
  the hosts' registration in both hosts. It also registers an `IDocumentPageCounter`, which both upload handlers
  need: the open `DocumentPageCounter` works on any `IPdfFormat` and `IWorkbookFormat`.
- To replace one format, register your implementation after the hosts' registration. Each interface except
  `IReportFileWriter` is one service, and the registration made last wins.
- A build can register several report writers. The report export uses the writer registered last for each format, so
  a Word writer registered after the built-in one replaces it. A build exports only the formats that it registers a
  writer for.
- `IReadableFormats` says which formats intake admits. The open build's `OpenBuildFormats` refuses email and Excel
  97-2003 workbooks. An implementation that reads either one also registers an `IReadableFormats` that admits it.
  Without that registration, intake still refuses those files.

## Check it

1. In your test project, reference `IniaAi.FileFormats.ContractTests`.
2. For each interface that you implement, subclass its contract. The abstract property returns your implementation.
   The contract fixtures copy next to your test project.
3. Run the tests. Your implementation is right when every subclass passes.
4. Register your implementation in `AddFileFormatsUnderTest()` in
   `platform/IniaAi.Testing.Documents/FileFormatsUnderTestServices.cs`, as the hosts do. Every test registration calls it, so
   every test that reads or writes documents through the build's formats then runs on your implementation.

The subclasses in `platform/IniaAi.FileFormats.Tests/Contracts/` run every contract against the open implementation and show
the pattern.


## What the Word contracts ask

- **Pages.** A page is your reader's own split of the text. A reader with a layout engine can take its layout pages,
  and one without can count words. Word renderers break pages differently anyway, so no caller relies on a page
  number. The contract asks for every word in order, several pages for a long document, the picture on the page of
  the text before it, and the same split of the same bytes on every call with the same fonts installed: a resumed
  run matches its stored pages by number.
- **The Word report.** `WordReportWriterContract` writes the reports that the export writes, through
  `WorkflowDocumentAdapter` and with the options the export passes: a page layout and a chart renderer. It reads the
  `.docx` as Word shows it, and checks the content, the headings, the lists, the links, the page layout, the chart
  pictures and the look of the report: section titles in bold capitals, blue subtitles, a rule before each section
  and justified body text. A title shows in capitals through Word's capitals property, so its text stays the text of
  the Markdown export. A link keeps its address only when it opens on its own (`http`, `https` or `mailto`): a
  relative link such as `/reports/42` shows only its text, because a downloaded file has no page of the platform to
  resolve it against. `MarkdownRenderer.WithoutRelativeLinks` does this to report HTML.
- **Reading a Word file in your own tests.** `WordFile` in `IniaAi.Testing.Ooxml` reads a `.docx` through its
  styles, lists and defaults, whatever library wrote it. The project references no document library. `WordFixtures` in `IniaAi.Testing.Documents` builds Word files with
  Open XML SDK, so a test needs no Word library of the build under test.


A Windows metafile (EMF) picture in a Word file stays out of the pages: no free image library decodes it.

## Test fixtures

Tests build their documents without a paid library. `PdfFixtures` writes PDF files byte by byte and encrypts them with
the PDF standard security handler. `OoxmlFixtures` builds workbooks with ClosedXML and encrypts them with
`OfficeEncryption`, Office's standard encryption. No free library writes an Excel 97-2003 workbook or a VBA project,
so `platform/IniaAi.Testing.Documents/Resources/` holds those, written once with a paid library.


## Done when

- Every contract subclass of your implementation passes.
- The tests that read or write documents pass with your implementation registered in `AddFileFormatsUnderTest()`.
