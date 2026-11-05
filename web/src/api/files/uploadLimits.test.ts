import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import { axiosError } from '@/test/helpers/axiosError'
import { EN } from '@/test/helpers/messages'
import { uploadLimitMessage } from './uploadLimits'

const { t } = createI18n({ legacy: false, locale: 'en', messages: { en: EN } }).global

const STORAGE_FULL = 'Your account has reached its storage limit. Delete documents you no longer need, or contact support.'
const NOW = new Date('2026-11-03T14:20:00Z')

const tooManyUploads = (resetsAt: string) =>
  axiosError(429, {
    title: 'Your organization has uploaded the most files allowed in an hour. You can upload more from 14:31 UTC.',
    status: 429,
    code: 'File.Refused.TooManyUploads',
    resetsAt,
  })

describe('uploadLimitMessage', () => {
  it("shows the platform's words for full storage", () => {
    const error = axiosError(403, { title: STORAGE_FULL, status: 403, code: 'File.Refused.StorageFull' })

    expect(uploadLimitMessage(error, t)).toBe(STORAGE_FULL)
  })

  it('says when one more upload fits, in the reader time zone', () => {
    const message = uploadLimitMessage(tooManyUploads('2026-11-03T14:31:00Z'), t, {
      now: NOW,
      locale: 'en-GB',
      timeZone: 'Asia/Dubai',
    })

    expect(message).toBe('Your organization has uploaded the most files allowed in an hour. You can upload more from 18:31 today.')
  })

  it('names tomorrow when the hour runs past the reader midnight', () => {
    const message = uploadLimitMessage(tooManyUploads('2026-11-03T20:30:00Z'), t, {
      now: new Date('2026-11-03T19:45:00Z'),
      locale: 'en-GB',
      timeZone: 'Asia/Dubai',
    })

    expect(message).toBe('Your organization has uploaded the most files allowed in an hour. You can upload more from 00:30 tomorrow.')
  })

  it('leaves any other refusal to its own handling', () => {
    expect(uploadLimitMessage(axiosError(403, { title: 'Forbidden' }), t)).toBeUndefined()
    expect(uploadLimitMessage(axiosError(429, { error: 'other', code: 'DailyAllowance.Exhausted', resetsAt: '2026-11-04T00:00:00Z' }), t)).toBeUndefined()
    expect(uploadLimitMessage(axiosError(400, { title: STORAGE_FULL, code: 'File.Refused.StorageFull' }), t)).toBeUndefined()
    expect(uploadLimitMessage(new Error('network down'), t)).toBeUndefined()
  })
})
