import { describe, expect, it } from 'vitest'
import { fullFileName } from './fileName'

describe('fullFileName', () => {
  it('appends the extension when the stored name lacks it', () => {
    expect(fullFileName({ name: 'Topics_10_Pages', extension: '.pdf' })).toBe('Topics_10_Pages.pdf')
  })

  it('does not double the extension when the stored name already carries it', () => {
    expect(fullFileName({ name: 'CIS_Reg_Fmk_8_25v2.pdf', extension: '.pdf' })).toBe(
      'CIS_Reg_Fmk_8_25v2.pdf',
    )
  })

  it('matches the extension regardless of case', () => {
    expect(fullFileName({ name: 'REPORT.PDF', extension: '.pdf' })).toBe('REPORT.PDF')
    expect(fullFileName({ name: 'report.pdf', extension: '.PDF' })).toBe('report.pdf')
  })

  it('trims a trailing space off the stem, as the server composition does', () => {
    expect(fullFileName({ name: 'Report.pdf  ', extension: '.pdf' })).toBe('Report.pdf')
    expect(fullFileName({ name: 'Report  ', extension: '.pdf' })).toBe('Report.pdf')
  })

  it('returns the name unchanged when there is no extension', () => {
    expect(fullFileName({ name: 'no_extension', extension: '' })).toBe('no_extension')
    expect(fullFileName({ name: 'no_extension', extension: undefined })).toBe('no_extension')
  })

  it('does not mistake a name that merely contains the extension for one that ends in it', () => {
    expect(fullFileName({ name: 'pdf_summary', extension: '.pdf' })).toBe('pdf_summary.pdf')
  })
})
