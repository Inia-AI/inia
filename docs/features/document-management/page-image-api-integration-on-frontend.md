---
type: explanation
status: current
description: "How the Vue app loads the image the platform renders for a document page: the PageService client, the usePageImage composable with its error copy and object-URL handling, the page preview dialog a cited page opens, and the page reader's original panel. Read when you work on page previews, a preview shows an error, or you add a new place that shows a page's original."
---

# Page Image API - Frontend Integration

The Vue app shows the image the platform renders for a document page in two places: the **page preview dialog** that a cited page opens, and the **original** panel of the Documents page reader. Both load the page through the `usePageImage` composable. The server side — which file types answer with an image and which with text, caching and the error codes — is on [Page Image API](../../reference/api/page-image.md). How citations reach a page button is on [Source Chain, Citations & Tracer (UI)](../../architecture/frontend/source-chain-and-citations.md).

## The pages API client

`PageService.getPageImage(pageId)` (`src/api/pages/service.ts`) is the one function that requests a page image. It extends `BaseService`, which adds the bearer token and the `X-Tenant-Id` header ([API layer](../../architecture/frontend/api-layer.md)).

- It checks the id against a UUID-shaped pattern (eight, four, four, four and twelve hex digits, any version) and throws `Invalid page ID format` before any request when the id does not match.
- It sends `GET api/v1/pages/{pageId}/image` with `responseType: 'blob'`.
- It returns `{ blob, contentType }`, taking `contentType` from the response's `Content-Type` header, or `application/octet-stream` when the header is missing.

The client is registered as `api.Pages` in `src/api/index.ts`. `PageApi.useGetPageImage()` (`src/api/pages/index.ts`) wraps the call in a Vue Query mutation, but nothing calls that hook: every caller goes through `usePageImage`, which calls `api.Pages.service.getPageImage` directly.

## Loading a page image

`usePageImage(pageId, active)` (`src/composables/usePageImage.ts`) loads the page while `active` is true and loads again when `pageId` changes. It returns `loading`, `error`, `imageUrl` and `text`.

- An `image/*` response becomes an object URL in `imageUrl`.
- A `text/*` response becomes `text`: for a file type with no page image, such as a Word or Excel file, the platform answers with the page's text.
- Any other content type sets `error` to the `unsupportedContentType` message.

It revokes the object URL when `active` turns false, before it loads another page, and when the component unmounts. A response that arrives after `active` turned false, or after a later request started, is dropped before it creates an object URL.

## The page preview dialog

`PagePreviewDialog` (`src/components/organisms/PagePreviewDialog/PagePreviewDialog.vue`) shows one cited page. It is built on the shared `ui/dialog`. `SourceChainPanel.vue`, which `App.vue` mounts once, hosts the one instance and opens it:

- from the panel's own document rows, and
- from a fact tooltip's page button: `useFactTooltip` dispatches `PAGE_PREVIEW_EVENT` (`page-preview:open`, `src/utils/pagePreviewEvents.ts`) with the page id, the title, the button that opened it, and the fact as a fallback.

The dialog's title is the document name and page, and also the image's alt text. While the request runs it shows **Loading...**; then the image, the page's text in a `<pre>`, or the error. Focus moves into the dialog and stays there. Escape or **Close** returns focus to the opener, or to the fallback when the opener is gone. A fact tooltip stays open under the dialog: while `isPagePreviewOpen()` holds, the tooltip ignores Escape and clicks.

## The page reader's original panel

`DocumentPageView` (`src/components/organisms/DocumentPageReader/DocumentPageView.vue`), the page reader on `/documents/:id`, calls `usePageImage(pageId, ref(true))` to show the page's original beside its text, with the same error messages ([Documents screen](screen-guide.md), [Documents: Upload, Preview & Viewer (UI)](../../architecture/frontend/document-handling.md)).

## Preview errors

`usePageImage` maps a failed request to a message under `shared.citations.errors` (`src/assets/locales/en/shared/citations.json`). A 401 never reaches it: the `BaseService` response interceptor clears the stored tokens and opens the login screen.

| The preview shows | Cause | What to do |
|---|---|---|
| "You don't have access to this document." | The API answered 403: the page's document belongs to another organization than the one selected (`X-Tenant-Id`). | Switch to the document's organization. |
| "This page is no longer available." | The API answered 404: the page, its document, or the stored file is gone. | Nothing to fix in the app; the document was deleted. |
| "Unsupported content type." | The response was neither `image/*` nor `text/*`. | Check the API response's `Content-Type` in the browser's network tab. |
| "Unable to load page preview. Please try again later." | Any other failure: a 500, a network error, or a page id that is not UUID-shaped. | Read the API log for the request; for a bad id, check the citation's `pageId`. |
| The page's text is the app's own HTML | `VITE_API_URL` is not set, so the request reached the Vite dev server, whose HTML answer is `text/html`. | Set `VITE_API_URL` to the API's address and restart the dev server. |

## Related pages

- [Page Image API](../../reference/api/page-image.md) — the endpoint, its content types, caching and errors
- [Source Chain, Citations & Tracer (UI)](../../architecture/frontend/source-chain-and-citations.md) — the citation model, fact tooltips and the Source Chain panel
- [Documents screen](screen-guide.md) — the page reader as a user sees it
