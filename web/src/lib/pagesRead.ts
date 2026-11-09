import { extensionPoints, type CountedFile } from '@/modules/extensionPoints'

/** The pages a run reads from an uploaded file: what its upload format says, else the pages counted for it. */
export const pagesRead = (file: CountedFile, requestFields: Record<string, unknown>): number | null | undefined => {
  for (const upload of extensionPoints.uploads) {
    const pages = upload.pagesRead?.(file, requestFields)
    if (pages !== undefined) {
      return pages
    }
  }
  return file.countedPages
}

/** The files as a run reads them: each with `countedPages` as the pages the run reads from it. */
export const withPagesRead = <T extends CountedFile>(files: T[], requestFields: Record<string, unknown>): T[] =>
  files.map(file => ({ ...file, countedPages: pagesRead(file, requestFields) }))
