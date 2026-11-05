import type { PageStatus } from '@/api/documents/schema'

export type ConversionTone = 'good' | 'warning' | 'failed' | 'pending'

const TONES: Record<string, ConversionTone> = {
  ProcessedSuccessfully: 'good',
  OCRSuccessful: 'good',
  ExtractedWithErrors: 'warning',
  OCRNeedsReview: 'warning',
  OCRFailed: 'failed',
  OCRFallback: 'pending',
}

const ICONS: Record<ConversionTone, string> = {
  good: 'circle-check',
  warning: 'exclamation-triangle',
  failed: 'xmark',
  pending: 'clock',
}

const LOCATIONS = new Set(['FullPage', 'LeftSide', 'RightSide'])

export const conversionTone = (status: PageStatus): ConversionTone => TONES[status] ?? 'pending'

export const conversionIcon = (status: PageStatus): string => ICONS[conversionTone(status)]

export const conversionLabelKey = (status: PageStatus): string =>
  `page.documents.detail.reader.conversion.${status in TONES ? status : 'NotProcessed'}`

export const locationLabelKey = (location: string): string | null =>
  LOCATIONS.has(location) ? `page.documents.detail.reader.location.${location}` : null
