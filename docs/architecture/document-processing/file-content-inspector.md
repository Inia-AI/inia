---
type: explanation
status: current
description: "The byte-level file inspector: what it detects, what it admits, what it refuses, which limits it enforces, where it runs, and how a check is added. Read when an upload or an email part is refused, or when you add an intake check or a format the platform reads."
---

# File content inspection

**On this page:** Where it runs · Rules per way in · The call and the verdict · The sequence · What the bytes decide · Limits · Encrypted files · Verification

`FileContentInspector` in `platform/IniaAi.FileFormats/Inspection/` decides from the bytes whether a file may enter the platform. It is a pure function over the bytes and the filename. It has no dependency on HTTP, the dispatcher or Entity Framework, so the same rules can run at an HTTP upload, in a Blazor upload card and on an email attachment.

## Where it runs

`DocumentIntakeGate` in `platform/IniaAi.Application/Documents/` is the one code path every mouth runs its bytes through before it stores them. It inspects the bytes, hands only an `Encrypted` verdict to the [unlock gate](index.md#password-protected-files-the-unlock-gate) by the container the verdict names, inspects the decrypted copy again, and reports the refusal to its `IIntakeRefusalRecorder` in the same call, so a refusal and its trail cannot come apart. The gate is one service holding the recorder and the deployment's `FileInspectionLimits`; every mouth injects it and passes an `IntakeMouth` that names the organization, the run when there is one, and whether the refusal is committed at once. A mouth that knows a file's size before it reads the bytes enters with the declared size and a reader (`ScreenAsync(fileName, declaredSize, readBytes, …)`, or the `IFormFile` overload): a file over `MaxFileSizeMb` is refused and recorded from the size alone, without a hash, and its bytes are never read. Every mouth in the table below except the email attachment intake, whose bytes are already in memory, enters that way. `AddInfrastructure` registers the gate with the platform's own recorder, `LoggingIntakeRefusalRecorder`, which writes a warning line; a closed module can register its own intake refusal recorder in its place. An admitted file comes back with the bytes to store and the detected type, and the mouth sets `Document.MediaType` from that type, so the processing pipeline reads a renamed file as what it is.


| Mouth | Refusal |
|---|---|
| `POST /api/v1/files` (`UploadFileCommandHandler`) | `400` with the key `File.Refused.<ReasonCode>` and the detail sentence; no document, file metadata or blob. An oversized upload is refused from its declared size before a byte is read. See the [Files API](../../reference/api/files.md#error-responses) |
| `POST /api/v1/workflow-executions/submit` (`SubmitWorkflowExecutionCommandHandler`) | `400` keyed `files[<index>]` with the file name and the detail sentence, one entry per failed file; every document the request created is deleted again, so a rejected submission leaves no new document behind; a document the upload reused stays. See the [submit endpoint](../../reference/api/submit-workflow-execution.md#per-file-errors) |
| The Blazor upload card (`DocumentUploadCard.razor`) | A notification naming the file and the reason; nothing is uploaded. An oversized file is refused from its declared size before a byte is read, and its record carries no hash |
| Email attachment intake inside a run (`EmailIntakeJobStage`) | The attachment is skipped with the reason, recorded like a password-protected part; the record carries the execution id. A refused body stops the email like a failed render |




The size and type checks left `UploadFileCommandValidator` for this gate: the validator runs before the handler, so a rejection raised there could never write its record. There is no extension allowlist anywhere. The file name never admits a file: it can only refuse one whose name claims a signed format the bytes are not ([refused on the name](#refused-on-the-name)), and it refines which text type a signature-less text file is.

A `Document` is born with its type. The production constructor is `Document(FileMetadata, MediaType)`, and every mouth passes the type the gate detected, so no mouth derives it from the file name; a block that writes its own output file names the type and the extension from the same choice. The one-argument constructor is internal to the test assemblies, so a production source cannot construct a document from its file name alone. A stored file is served with the type its document carries (`FileMetadata.ContentType` reads `Document.MediaType` when the document is loaded), so a PDF named `.exe` downloads as a PDF. The name carries the detected type's extension too: when a file's extension does not name the detected type, the type's extension replaces it (`MediaTypeExtensions.ServedFileName`), so a PDF uploaded as `report.exe` is stored and served as `report.pdf`, and a text file named `notes.log` as `notes.txt`. An extension that already names the type stays, such as `.jpeg` for a JPEG or `.md` for Markdown. The upload applies the rule to the stored name, and every download applies it again when it serves the file (`ServedExtension`), so a document stored before the rule, or by another intake path, downloads under its type's extension as well. A refusal decided from the declared size before any byte was read goes to the recorder as `IntakeRefusal.Unread`, and the module's row for it carries a null `ContentSha256`.


Email expansion (`EmailExpansionService`) runs the inspector once more, on each attachment and each ZIP entry, but only to read the detected type: it expands the five types it supports (PDF, Word, CSV, Excel, ZIP) and skips a recognised type it does not (an image, plain text, a nested email). Bytes no detector names are not skipped there; the part goes on to the intake gate, which refuses it and reports the refusal to its recorder. At expansion, the part's name and declared MIME type decide nothing except which text type a bare text part is; at the gate, a part whose name claims a signed format its bytes are not is refused like an upload.

## Rules per way in

Every way in admits by the bytes, through the same gate, and none runs a malware scanner: a file the checks admit is stored as it is, and a refused file is not stored and its refusal is recorded (see [Where it runs](#where-it-runs) for how each answers). The types are the ones the pipeline reads: PDF, JPEG, PNG, CSV, Excel (`.xlsx`, `.xlsm`, and `.xls` in the full edition), Word (`.docx`), plain text and Markdown, and in the full edition an RFC 822 or Outlook email.

| Way in | Admitted types | What the file name does | Size | Archives |
|---|---|---|---|---|
| `POST /api/v1/files` | The pipeline's types | Refuses only a name that claims a signed format the bytes are not; it is stored, with control characters turned into spaces, and shown | `MaxFileSizeMb` (100 MiB unless set); the request body is bounded by the same figure plus 64 KiB before the form is read (`FileUploadSizeLimitAttribute`) | A ZIP is not a type the pipeline reads; a Word or Excel package must keep to the [limits](#limits) |
| `POST /api/v1/workflow-executions/submit` | As the files endpoint, file by file; an email is expanded first | As the files endpoint | `MaxFileSizeMb` per file; the request at most 200 MB | As the files endpoint |
| The Blazor upload card | The pipeline's types | As the files endpoint | `MaxFileSizeMb`, from the declared size | As the files endpoint |
| An email's attachment (`EmailExpansionService`, `EmailIntakeJobStage`) | PDF, Word, CSV, Excel and ZIP are kept; another recognised type is skipped with a reason; bytes no detector names go on to the gate, which refuses them | Names the stored part; decides only which text type a bare text part is | `MaxFileSizeMb` per part | A ZIP attachment meets the gate's [limits](#limits) first, then at most `EmailExpansion:MaxArchiveEntries` (100) entries, each at most `MaxFileSizeMb`; a nested ZIP, a folder entry, macOS metadata and a symbolic link are not content; an entry keeps only its own name, never a folder |



## The call and the verdict

```csharp
FileInspection verdict = FileContentInspector.Default.Inspect(bytes, fileName, limits);
```

`limits` is required. A caller builds it with `FileInspectionLimits.FromSettings(platformSettings.IniaAi)`, so the file-size ceiling is the deployment's `MaxFileSizeMb`; `FileInspectionLimits.Default` is that factory applied to the settings defaults and exists for tests.

| Field | Meaning |
|---|---|
| `Outcome` | `Accepted`, `Refused` or `Encrypted` |
| `ReasonCode` | An `IntakeRefusalReasons` value (`FileTypeMismatch`, `EmptyFile`, `FileTooLarge`, `StructuralLimit`, `MalformedFile`). Set on a refusal only. The same code fits a 4xx body and the `ScreeningRecords.ReasonCode` column |
| `Detail` | One sentence for the caller's response or log. Set on a refusal and on an encrypted verdict |
| `DetectedMediaType` | The IANA media type the bytes are, from detection. `application/octet-stream` when nothing recognised the bytes. Null only when the sequence stopped before detection |
| `MediaType` | The platform `MediaType` enum value for that string, when the enum has one. A refused zip carries `ApplicationZip` here; admission is a separate question |
| `HasMacros` | True when an Excel workbook carries a VBA project. A macro file is admitted, and every mouth that stores a document copies this mark onto it (`Documents.HasMacros`) |
| `Container` | Set on an encrypted verdict: `Pdf`, `LegacyWorkbook` or `OfficePackage`. The caller hands the bytes to unlock by this value, not by the filename |

`Encrypted` is not a refusal. The caller routes the bytes to the unlock gate (`IDocumentUnlocker`) and inspects the decrypted copy. A PDF whose content merely contains the literal `/Encrypt` also gets this verdict; unlock then reports it as not encrypted, and the caller proceeds with the bytes it has.

### Reason codes a refusal can carry

| Code | When |
|---|---|
| `EmptyFile` | Zero bytes |
| `FileTooLarge` | More bytes than `MaxFileBytes` |
| `MalformedFile` | The bytes start like a zip or compound-file container and the container cannot be read, an image header cannot be read, or a PDF is a header with nothing behind it: no object in the first 64 KiB and no `startxref` in the last 4 KiB. A damaged PDF that still carries objects is admitted; the reader downstream repairs it, and the upload response carries a warning |
| `StructuralLimit` | The zip directory declares too many entries, too many expanded bytes, too high a ratio or an oversized package manifest, or an image declares too many pixels |
| `FileTypeMismatch` | The bytes are not a type the platform reads, or the name claims a signed format the bytes are not |
| `FormatNotRead` | The build reads no file of the detected type: the open build refuses `.eml`, `.msg` and `.xls`. The intake gate decides it after inspection, from `IReadableFormats`, and the refusal stands also when the sender vouches for the file |

## The sequence

The inspector is an ordered list of independent checks over the same bytes. Each check returns a verdict to stop the sequence or null to pass. The default order runs the cheap checks first and refuses a structural bomb before any check expands content.

| Order | Check | Reads | Can return |
|---|---|---|---|
| 1 | `SizeLimitCheck` | The byte count | `EmptyFile`, `FileTooLarge` |
| 2 | `ArchiveStructureCheck` | The entry count from the end-of-central-directory record at the tail, then the central directory, when the bytes start with `PK` | `MalformedFile`, `StructuralLimit` |
| 3 | `ContentTypeDetectionCheck` | Signatures through FileSignatures 7.3, then a text sample | `MalformedFile`. Otherwise it records the detected type on the context and passes |
| 4 | `EncryptionCheck` | The PDF for its `/Encrypt` key; the compound-file directory through OpenMcdf for an `EncryptedPackage` stream or a legacy workbook password record | `Encrypted` |
| 5 | `AdmissionCheck` | The detected type | `FileTypeMismatch` |
| 6 | `ClaimedFormatCheck` | The format family the file name claims, against the detected type | `FileTypeMismatch` |
| 7 | `PdfStubCheck` | The first 64 KiB and the last 4 KiB of a PDF | `MalformedFile` |
| 8 | `ImageDimensionCheck` | The image header, for JPEG and PNG | `MalformedFile`, `StructuralLimit` |

### Adding a check

Implement `IFileContentCheck` and add the element to the sequence. Callers and the verdict type do not change. A check that reads the detected type calls `context.RequireDetected()`, which throws if the check was placed before detection, so a misordered sequence fails on its first use rather than refusing silently. Virus scanning and PDF active-content inspection (embedded JavaScript, launch actions) are the two additions the design names. Neither is built.

```csharp
var inspector = new FileContentInspector([.. FileContentInspector.DefaultChecks, new MyCheck()]);
```


### The email chain

`FileContentInspector.EmailMessages` is the sequence for an email drop: size, archive structure, detection and `AdmissionCheck` as above, then `EmailAdmissionCheck`, and no encryption step, because an email is never an encrypted container. `AdmissionCheck` keeps its wording for a type the platform never reads ("The platform does not accept this type of file (application/vnd.microsoft.portable-executable). Upload a PDF, Word, Excel, CSV, PNG or JPEG file instead."); `EmailAdmissionCheck` refuses a readable document that is not an RFC 822 or Outlook message as `FileTypeMismatch` with "Only email files (.eml or .msg) are accepted here. Use the email itself, not one of its attachments." Both refusals go to the recorder like any other.


## What the bytes decide

Admission keys off the detected type. FileSignatures names each type by the same IANA string the platform `MediaType` enum carries as its member value, so the platform type is a parse of that string. The admitted set is the one the document pipeline reads: every type in `MediaTypeExtensions.ProducesPages`, plus the two email types it converts first. The open build reads no email and no Excel 97-2003 workbook, so after the inspector admits one of those three types, `DocumentIntakeGate` refuses it as `FormatNotRead` with a message that names the format and what to do instead, such as "The platform does not read email files (.eml). Save the email as a PDF, or upload its attachments instead. The full edition reads email files directly and extracts their attachments." The refusal stands on every mouth, a trusted editor's upload in the workflow editor included, and nothing is stored.

| Bytes | Detected as | Admitted as |
|---|---|---|
| `%PDF` | `application/pdf` | `ApplicationPdf` |
| JPEG start-of-image marker | `image/jpeg` | `ImageJpeg` |
| PNG signature | `image/png` | `ImagePng` |
| Zip package with `xl/workbook.xml` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | `ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet`; `HasMacros` when a `vbaProject.bin` entry is present |
| Zip package declaring the macro-enabled workbook content type | `application/vnd.ms-excel.sheet.macroEnabled.12` | `ApplicationVndMsExcelSheetMacroEnabled12`; `HasMacros` when a `vbaProject.bin` entry is present, so a workbook merely saved as `.xlsm` is not marked |
| Zip package with `word/document.xml` | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | `ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument` |
| Compound file with a `Workbook` stream | `application/vnd.ms-excel` | `ApplicationVndMsExcel`; `HasMacros` when a `_VBA_PROJECT_CUR` or `Macros` storage is present. Paid build only |
| Compound file with a `__properties_version1.0` stream | `application/vnd.ms-outlook` | `ApplicationVndMsOutlook`. Paid build only |
| Text whose first line is a mail header and whose header block, ending in a blank line, names a `From` or `Received` header | `message/rfc822` | `MessageRfc822`. Paid build only |
| Other text | `text/csv`, `text/markdown` or `text/plain` by extension | `TextCsv`, `TextMarkdown` or `TextPlain` |

The filename never admits a file. It has two roles: it refines which text type a signature-less text file is, and it can refuse a file whose bytes are not the format it claims ([below](#refused-on-the-name)). A valid PDF named `report.exe` is admitted as a PDF, because `.exe` claims no signed format. A file with no extension is admitted on its bytes.

Text has no signature, so the test is negative: no NUL byte and at most two percent control characters in the first 64 KiB, walked as bytes so junk allocates nothing. A UTF-16 sample with a byte-order mark is transcoded first, because its ASCII characters carry NUL high bytes; without the mark those NULs make it unrecognised. The encoding is not otherwise validated. The readers downstream decode with replacement, so a legacy Windows code page that works today still passes.

### Refused on the bytes

| Bytes | Detected as | Why refused |
|---|---|---|
| `MZ` executable named `.pdf` | `application/vnd.microsoft.portable-executable` | Not a type the platform reads |
| Plain zip named `.docx` | `application/zip` | The package has no Office part |
| Legacy `.doc` compound file | `application/msword` | The platform does not page `.doc` |
| Macro-enabled Word `.docm` | `application/vnd.ms-word.document.macroEnabled.12` | Not a type the platform reads |
| `%PDF-1.4` with nothing behind it | `application/pdf` | A header, not a document: no object and no cross-reference table |
| GIF named `.png` | `image/gif` | Not a type the platform reads |
| Bytes no signature or text rule matches | `application/octet-stream` | Unrecognised |

### Refused on the name

`ClaimedFormatCheck` runs after the allowlist. When the file name claims a format with a signature and the bytes are an admitted type of another family, the file is refused as `FileTypeMismatch` with a sentence naming both, for example "The file is named as a PDF, but its content is plain text. Check that it is the right file, or rename it to match its content." A mismatch is the strongest sign the wrong file was attached, and text under a `.pdf` name would otherwise be admitted as text.

| The name claims | Family | Admitted under the name |
|---|---|---|
| `.pdf` | a PDF | PDF |
| `.docx`, `.doc` | a Word document | Word |
| `.xlsx`, `.xlsm`, `.xls` | a spreadsheet | any Excel format: a workbook saved as the other Excel format is still the spreadsheet its sender meant |
| `.jpg`, `.jpeg`, `.png` | an image | JPEG or PNG |
| `.msg` | an email | an Outlook or RFC 822 message |

A name that claims none of these, including `.eml`, `.txt`, `.csv`, `.md`, `.exe` and no extension, claims nothing and never refuses. The cost is accepted: a CSV that a legacy system exported under an `.xls` name is refused and must be renamed. The email chain does not run this check; its own admission check decides.


Macro presence never refuses. Macro-bearing workbooks are ordinary customer documents; the flag is metadata.

## Limits

`FileInspectionLimits` carries the ceilings. A caller overrides one with `FileInspectionLimits.Default with { ... }`.

| Limit | Default | Read from |
|---|---|---|
| `MaxFileBytes` | `MaxFileSizeMb` from the platform settings, 100 MiB when unset | The byte count |
| `MaxArchiveEntries` | 10 000 | The zip central directory |
| `MaxExpandedBytes` | 1 GiB | The sum of declared entry lengths |
| `MaxCompressionRatio` | 100 : 1 | Declared expanded bytes over file bytes, applied only above `CompressionRatioFloorBytes` |
| `MaxPackageManifestBytes` | 1 MiB | The declared length of `[Content_Types].xml`, the one entry the signature library materialises to tell a macro-enabled package from a plain one |
| `CompressionRatioFloorBytes` | 10 MiB | A small Office file of near-empty XML compresses far better than any bomb needs to |
| `MaxImagePixels` | 100 megapixels | Width times height from the image header |

The entry count is read from the end-of-central-directory record before the directory itself, because reading the directory of an archive made of nothing but entries costs memory proportional to that count. The sizes then come from the directory; no entry is opened to measure it, and the runtime never lets an entry produce more than its declared length. Tests pin that refusing a 16 MiB bomb allocates under 1 MiB, refusing a 10 001-entry archive allocates under 64 KiB, and refusing a package with a 4 MiB manifest allocates under 512 KiB.

## Encrypted files

| Bytes | Signal |
|---|---|
| Encrypted Office package (`.docx`, `.xlsx`, `.xlsm`) | A compound file with an `EncryptedPackage` or `EncryptionInfo` stream. The document type inside is known after unlock, so `DetectedMediaType` is `application/octet-stream` |
| Encrypted PDF | A `/Encrypt` key in the file |
| Encrypted legacy workbook | A `FILEPASS` record among the first records of the `Workbook` stream |

The compound-file questions go through OpenMcdf, not through Aspose's format detector. Aspose walks the sector counts a header declares rather than the bytes present, so six mutated header bytes cost it tens of gigabytes, while OpenMcdf sizes its reads from the stream and reads the same bytes in milliseconds. Tests pin that such a file is decided within two seconds by the inspector, within a second by the unlock gate, and within a second at the API mouth.

`EncryptedContainerProbe` answers the question for the check and for the unlock gate alike, so the unlock gate dispatches on the same signal and never on the file name. The verdict is `Encrypted` with the container kind and a detail sentence. The unlock gate is described on the [document processing page](index.md#password-protected-files-the-unlock-gate). A PDF with owner-only restrictions and no user password gets the verdict too; unlock strips it without a prompt, as it did before the inspector existed.


## Verification

- `FileContentInspectorTests` in `platform/IniaAi.FileFormats.Tests/Inspection/`: the renamed executable, the plain zip as `.docx`, a legacy workbook and an Outlook message told apart by their bytes, content of another family under a signed name refused naming both (text as `.pdf`, a PDF as `.docx`, an image as `.pdf`, text as `.xls` and `.jpg`, a workbook as `.msg`), content of the claimed family admitted under a sibling name, `.doc`, the zip bomb with its allocation bound, the entry-count guard with its allocation bound, the truncated zip, the macro workbook, encrypted PDF, workbook, legacy workbook and Word document, every accepted format from a fixture, the whole fixture corpus, a GIF as `.png`, binary junk as `.csv`, Windows-1252 text, text named `.eml`, the empty file, the size limit, the oversized image header, a check appended to the sequence, a check placed before detection, a PDF header with nothing behind it refused as malformed, and a PDF with objects but no trailer still admitted.
- `DocumentConstructionTests` in `IniaAi.Domain.Tests/`: a document born from detected bytes carries that type whatever the name says; the compiler keeps the one-argument constructor out of production code.
- `UploadFileCommandHandlerIntakeTests` in `IniaAi.Application.UnitTests/` against a real database: a renamed executable refused with its reason and no row or blob, an empty and an oversized file refused with their reasons, an encrypted PDF admitted as the decrypted copy, an encrypted PDF without a password asked for one with no record, an owner-only PDF admitted without a prompt, the mutated compound file named `.xlsx` decided within a second, a PDF named `.exe` stored as a PDF, and plain text named `.pdf` and an Outlook message named `.xls` each refused naming both formats with no row or blob.
- `EmailIntakeJobStageTests` in `IniaAi.Services.Tests/`: a hostile attachment skipped with its reason, a refused body failing the email, and a minted part carrying its detected type.
- `SubmitWorkflowExecutionCommandHandlerTests` and `SubmitWorkflowExecutionEmailExpansionTests` in `IniaAi.Application.UnitTests/`: a failed file reported under `files[<index>]` with the accepted siblings deleted, every file refused reported one per file with nothing to delete, a refused body or raw email keyed under the email's index with the other parts deleted, and an email that fails conversion removing the uploads made before it.
- `FileContentInspectorSampleRunTests` reads a directory of real documents in place when `INIA_INSPECTOR_SAMPLE_DIR` names it, caps at 100 files per extension (`INIA_INSPECTOR_SAMPLE_PER_EXTENSION` overrides) and 30 MiB per file, and fails on any refusal of a file with an accepted extension. It skips when the variable is unset.




