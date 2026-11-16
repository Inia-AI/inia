---
type: reference
status: current
description: "Documents GET /pages/{id}/image: renders a document page as a PNG, returns an image page's original bytes, or returns the page text, with its caching headers and errors. Read when you show a cited page to a user or debug a page preview that does not load."
---

# Page Image API

**On this page:** Overview · Request · Response · Error Responses · PDF Page Rendering Details · Caching Behavior · Data Model Context · Usage Guidance · Example Request Flow · Related Endpoints · Related pages

## Overview

The Page Image API renders document pages as images for citation verification in workflow reports. When users view a report, they can click on cited sources to see the original page content.

**Endpoint:** `GET /api/v1/pages/{pageId}/image`

**Authentication:** Required (JWT Bearer token and `X-Tenant-Id`)

**Authorization:** User must belong to the same organization as the page's parent document.

---

## Request

### Path Parameters

| Parameter | Type   | Required | Description                          |
|-----------|--------|----------|--------------------------------------|
| `pageId`  | string | Yes      | GUID identifier of the page to render |

### Headers

| Header          | Required | Description                                      |
|-----------------|----------|--------------------------------------------------|
| `Authorization` | Yes      | Bearer token for authentication                  |
| `X-Tenant-Id`   | Yes      | The organization that owns the page's document   |
| `If-None-Match` | No       | ETag value for conditional GET (cache validation)|

---

## Response

### Success Response (200 OK)

The response body contains the raw binary content. The `Content-Type` header indicates the format.

#### Response Headers

| Header          | Description                                                |
|-----------------|------------------------------------------------------------|
| `Content-Type`  | MIME type of the content (see Content Types below)         |
| `Cache-Control` | `private, max-age=3600` - browser may cache for 1 hour     |
| `ETag`          | Quoted hash of the response body: the first 22 characters of its Base64 SHA-256 (`GenerateETag`) |
| `Last-Modified` | RFC 1123 date of the file metadata record's `UpdatedAt` (else `CreatedAt`); sent for PDF and image responses only, not for text responses |

#### Content Types

The response content type depends on the source document. An image page is returned with the content type stored with its file (`FileMetadata.ContentType`), shown below for the usual uploads:

| Source Document Type | Response Content-Type      | Response Body              |
|----------------------|----------------------------|----------------------------|
| PDF                  | `image/png`                | PNG image of the page      |
| PNG image            | `image/png`                | Original image bytes       |
| JPEG image           | `image/jpeg`               | Original image bytes       |
| GIF image            | `image/gif`                | Original image bytes       |
| WebP image           | `image/webp`               | Original image bytes       |
| BMP image            | `image/bmp`                | Original image bytes       |
| TIFF image           | `image/tiff`               | Original image bytes       |
| Word (.docx)         | `text/plain; charset=utf-8`| UTF-8 encoded page text    |
| Excel (.xlsx)        | `text/plain; charset=utf-8`| UTF-8 encoded page text    |
| Other formats        | `text/plain; charset=utf-8`| UTF-8 encoded page text    |

### Conditional Response (304 Not Modified)

If the client sends an `If-None-Match` header matching the current `ETag`, the server returns:
- Status: `304 Not Modified`
- Body: Empty

This indicates the client's cached version is still valid.

---

## Error Responses

Errors from the handler return a ProblemDetails JSON body whose `title` is the error description and whose `code` extension is the error code (`ApiController.Problem`):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Page not found.",
  "status": 404,
  "code": "General.NotFound"
}
```

A validation failure (400) returns the validation-problem shape instead, with an `errors` object keyed by field.

### Error Status Codes

| Status | Condition                                          | Error Description                |
|--------|----------------------------------------------------|---------------------------------|
| 400    | Empty `pageId` (a malformed or unknown id returns 404) | Validation error details    |
| 401    | Missing or invalid authentication token            | (Standard auth error)           |
| 403    | `X-Tenant-Id` names no organization the caller belongs to | (Standard tenant error)    |
| 404    | Page does not exist, or its document belongs to another organization | "Page not found." |
| 404    | The page's document was deleted, and the caller is in its organization. Code `Document.Deleted` | "The document was deleted, so its pages can no longer be shown." |
| 404    | The page's document is gone for any other caller  | "Page not found."               |
| 404    | Document's file metadata not found                 | "File metadata not found."      |
| 404    | File storage path is missing                       | "File storage path not found."  |
| 500    | Error during image generation (PDF rendering, etc.)| "Error generating page image."  |

---

## PDF Page Rendering Details

When the source document is a PDF:

1. **Page Number Mapping**: The API uses `FilePageNumber` (the original page number in the PDF file), not `PageNumber` (the logical page number in the document flow).

2. **Split Page Support**: For documents with detected double-page layouts (e.g., scanned books), the page may have a `Location` property:
   - `FullPage` - Renders the entire PDF page
   - `LeftSide` - Renders only the left half of the PDF page
   - `RightSide` - Renders only the right half of the PDF page

3. **Resolution**: Pages are rendered at 100 DPI, providing a balance between quality and file size.

4. **Format**: Always returns PNG regardless of original PDF content.

---

## Caching Behavior

### Browser Caching

The API returns `Cache-Control: private, max-age=3600` which means:
- `private`: Only the user's browser should cache (not shared caches/CDNs)
- `max-age=3600`: Cache is valid for 1 hour (3600 seconds)

### ETag Validation

The `ETag` header contains a hash of the response content. The server still builds the response (including any PDF render) before it compares the first `If-None-Match` value to the `ETag` exactly. To validate a cached response:

1. Store the `ETag` value when first fetching the image
2. On subsequent requests, send `If-None-Match: <stored-etag>`
3. If `304 Not Modified` is returned, use the cached version
4. If `200 OK` is returned, update the cache with new content and ETag

### Cache Key Considerations

The same `pageId` will return the same content unless:
- The source file is replaced
- The page's location property changes
- The page's text changes (text responses only)

---

## Data Model Context

Understanding the data relationships helps when debugging:

```
Page
├── PageNumber (logical position in document, 1-based)
├── FilePageNumber (position in source PDF, 1-based)
├── Location (FullPage | LeftSide | RightSide)
├── Text (extracted text content)
└── Document
    ├── MediaType (PDF, Word, Image, etc.)
    ├── OrganizationId (tenant isolation)
    └── FileMetadata
        ├── StorageProvider (Database | S3 | MinIO)
        ├── ExternalStoragePath (file location key)
        └── Extension (.pdf, .docx, .png, etc.)
```

---

## Usage Guidance

### When to Call This API

- User clicks on a citation/source reference in a workflow report
- User wants to verify the original content that was used to generate a report section
- Displaying page previews in a document viewer

### Handling Different Response Types

Since the response can be either binary image data or text, check the `Content-Type` header:

- `image/*` → Display as image
- `text/plain*` → Display as formatted text (the source was not a PDF/image)

### Error Handling Recommendations

| Error | Recommended UX                                                    |
|-------|-------------------------------------------------------------------|
| 404   | The platform's own sentence when the code is `Document.Deleted`; otherwise "This page is no longer available" or fall back to showing text from workflow execution data |
| 500   | "Unable to load page preview. Please try again later."            |

### Performance Considerations

- PDF rendering happens on demand on every request; the server caches nothing, and it renders the page even when the `If-None-Match` matches.
- A response stays in the browser cache for an hour (`max-age=3600`).
- An image page returns the stored bytes with no rendering.

---

## Example Request Flow

```
1. Client: GET /api/v1/pages/abc123/image
   Headers: Authorization: Bearer <token>

2. Server: 200 OK
   Headers:
     Content-Type: image/png
     Cache-Control: private, max-age=3600
     ETag: "n4bQgYhMfWWaL+qgxVrQFa"
     Last-Modified: Sat, 21 Dec 2024 10:30:00 GMT
   Body: <PNG binary data>

3. Client caches response with ETag

4. Later - Client: GET /api/v1/pages/abc123/image
   Headers:
     Authorization: Bearer <token>
     If-None-Match: "n4bQgYhMfWWaL+qgxVrQFa"

5. Server: 304 Not Modified
   Body: <empty>

6. Client uses cached PNG
```

---

## Related Endpoints

| Endpoint                      | Description                              |
|-------------------------------|------------------------------------------|
| `GET /api/v1/files/{id}`      | Download original source file            |
| `GET /api/v1/workflow-executions/{id}/logs/{logId}` | Full detail of one execution log (prompt, output, placeholder values) |

## Related pages

- [Documents screen](../../features/document-management/screen-guide.md) — the web app pages where these pages are uploaded, processed, inspected, and re-processed.
