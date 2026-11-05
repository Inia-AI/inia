export const PAGE_PREVIEW_EVENT = 'page-preview:open'

export interface PagePreviewEventDetail {
  pageId: string
  title: string
  opener: HTMLElement
  fallback?: HTMLElement
}

export const isPagePreviewOpen = (): boolean => document.querySelector('[data-page-preview]') !== null
