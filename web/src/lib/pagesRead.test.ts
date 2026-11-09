import { afterEach, describe, expect, it } from 'vitest'
import { extensionPoints, type UploadFormats } from '@/modules/extensionPoints'
import { pagesRead, withPagesRead } from './pagesRead'

const report = { name: 'report.pdf', countedPages: 12 }
const added: UploadFormats[] = []

const register = (upload: UploadFormats) => {
  extensionPoints.uploads.push(upload)
  added.push(upload)
}

describe('pagesRead', () => {
  afterEach(() => {
    for (const upload of added.splice(0)) {
      extensionPoints.uploads.splice(extensionPoints.uploads.indexOf(upload), 1)
    }
  })

  it('reads the counted pages when no upload format covers the file', () => {
    expect(pagesRead(report, {})).toBe(12)
  })

  it('reads what an upload format says for a file it covers', () => {
    register({ extensions: ['pdf'], panel: null, pagesRead: file => (file.countedPages ?? 0) * 2 })

    expect(pagesRead(report, {})).toBe(24)
  })

  it('falls back to the counted pages when the format leaves the file alone', () => {
    register({ extensions: ['eml'], panel: null, pagesRead: () => undefined })

    expect(pagesRead(report, {})).toBe(12)
  })

  it('gives each file of a run the pages the run reads from it, keeping the rest of the file', () => {
    register({ extensions: ['eml'], panel: null, pagesRead: (file, fields) => (fields.extract && file.name.endsWith('.eml') ? 9 : undefined) })
    const files = [{ ...report, ref: 'r1' }, { name: 'q3.eml', countedPages: 1, ref: 'r2' }]

    expect(withPagesRead(files, { extract: true })).toEqual([
      { name: 'report.pdf', countedPages: 12, ref: 'r1' },
      { name: 'q3.eml', countedPages: 9, ref: 'r2' },
    ])
  })
})
