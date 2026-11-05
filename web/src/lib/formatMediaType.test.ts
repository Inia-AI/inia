import { describe, expect, it } from 'vitest'
import {
  MEDIA_TYPE_GROUPS,
  formatMediaType,
  mediaTypeFilterByGroup,
  mediaTypeGroup,
  mediaTypeIconName,
  previewKindOf,
} from './formatMediaType'

describe('formatMediaType', () => {
  it('returns an em-dash for nullish', () => {
    expect(formatMediaType(null)).toBe('—')
    expect(formatMediaType(undefined)).toBe('—')
    expect(formatMediaType('')).toBe('—')
  })

  it('groups known backend MediaType values into friendly labels', () => {
    expect(formatMediaType('ApplicationPdf')).toBe('PDF')
    expect(formatMediaType('ImageJpeg')).toBe('Image')
    expect(formatMediaType('ImagePng')).toBe('Image')
    expect(formatMediaType('TextCsv')).toBe('CSV')
    expect(formatMediaType('TextTabSeparatedValues')).toBe('CSV')
    expect(formatMediaType('ApplicationVndMsExcel')).toBe('Excel')
    expect(formatMediaType('ApplicationVndMsExcelSheetMacroEnabled12')).toBe('Excel')
    expect(
      formatMediaType('ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet'),
    ).toBe('Excel')
    expect(formatMediaType('ApplicationMsWord')).toBe('Word')
    expect(
      formatMediaType('ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument'),
    ).toBe('Word')
    expect(formatMediaType('ApplicationVndMsPowerpoint')).toBe('PowerPoint')
    expect(formatMediaType('TextPlain')).toBe('Text')
    expect(formatMediaType('ApplicationJson')).toBe('Text')
    expect(formatMediaType('ApplicationZip')).toBe('Archive')
    expect(formatMediaType('AudioMp3')).toBe('Audio')
    expect(formatMediaType('VideoMp4')).toBe('Video')
    expect(formatMediaType('MessageRfc822')).toBe('Email')
  })

  it('falls through to the raw value for anything unrecognised', () => {
    expect(formatMediaType('SomeWeirdFuturePrefixFoo')).toBe('SomeWeirdFuturePrefixFoo')
  })
})

describe('mediaTypeGroup', () => {
  it('classifies PDF, CSV, and Excel without collision', () => {
    expect(mediaTypeGroup('ApplicationPdf')).toBe('pdf')
    expect(mediaTypeGroup('TextCsv')).toBe('csv')
    expect(mediaTypeGroup('ApplicationVndMsExcel')).toBe('excel')
  })

  it('puts TextCsv in csv, not text', () => {
    expect(mediaTypeGroup('TextCsv')).toBe('csv')
    expect(mediaTypeGroup('TextPlain')).toBe('text')
  })

  it('unknown for anything unmatched', () => {
    expect(mediaTypeGroup('FooBar')).toBe('unknown')
    expect(mediaTypeGroup(null)).toBe('unknown')
  })
})

describe('mediaTypeIconName', () => {
  it('maps groups to registered FA icons', () => {
    expect(mediaTypeIconName('ApplicationPdf')).toBe('file-pdf')
    expect(mediaTypeIconName('ImageJpeg')).toBe('file-image')
    expect(mediaTypeIconName('TextCsv')).toBe('file-csv')
    expect(mediaTypeIconName('ApplicationVndMsExcel')).toBe('file-excel')
    expect(mediaTypeIconName('ApplicationZip')).toBe('file-archive')
  })

  it('falls back to the generic file icon', () => {
    expect(mediaTypeIconName('FooBar')).toBe('file')
    expect(mediaTypeIconName(null)).toBe('file')
  })
})

describe('previewKindOf', () => {
  it('returns pdf for PDF media types', () => {
    expect(previewKindOf('ApplicationPdf')).toBe('pdf')
  })

  it('returns image for image media types', () => {
    expect(previewKindOf('ImageJpeg')).toBe('image')
    expect(previewKindOf('ImagePng')).toBe('image')
  })

  it('returns null for non-previewable types', () => {
    expect(previewKindOf('ApplicationVndMsExcel')).toBeNull()
    expect(previewKindOf('ApplicationZip')).toBeNull()
    expect(previewKindOf('TextPlain')).toBeNull()
    expect(previewKindOf('ApplicationMsWord')).toBeNull()
  })

  it('returns null for nullish input', () => {
    expect(previewKindOf(null)).toBeNull()
    expect(previewKindOf(undefined)).toBeNull()
    expect(previewKindOf('')).toBeNull()
  })
})

describe('mediaTypeFilterByGroup', () => {
  it('produces an OData fragment for every exposed group', () => {
    for (const g of MEDIA_TYPE_GROUPS) {
      expect(mediaTypeFilterByGroup(g.id)).toBeTruthy()
    }
  })

  it('returns null for unknown group ids', () => {
    expect(mediaTypeFilterByGroup('nope')).toBeNull()
  })

  it('pdf uses an exact eq', () => {
    expect(mediaTypeFilterByGroup('pdf')).toBe("mediaType eq 'ApplicationPdf'")
  })

  it('image uses startswith', () => {
    expect(mediaTypeFilterByGroup('image')).toBe("startswith(mediaType, 'Image')")
  })

  it('excel combines contains fragments', () => {
    expect(mediaTypeFilterByGroup('excel')).toBe(
      "(contains(mediaType, 'Excel') or contains(mediaType, 'Spreadsheet'))",
    )
  })
})
