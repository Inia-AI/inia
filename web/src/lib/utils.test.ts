import { afterEach, describe, expect, it, onTestFinished, vi } from 'vitest'
import {
  clearSignIn,
  cn,
  formatDate,
  formatDateTime,
  formatLongDateTime,
  formatDuration,
  formatDurationMs,
  formatMonth,
  formatRelativeToNow,
  getDecodedToken,
  getObservationStatusClass,
  hasMultipleTenants,
  hasObservationMessages,
  hasRole,
  isTokenValid,
  stripXmlTags,
} from './utils'
import { makeJwt } from '@/test/helpers/jwt'

afterEach(() => {
  localStorage.clear()
})

describe('cn', () => {
  it('merges class strings', () => {
    expect(cn('a', 'b')).toBe('a b')
  })

  it('lets tailwind-merge dedupe conflicting utilities', () => {
    expect(cn('p-2', 'p-4')).toBe('p-4')
  })

  it('drops falsy values', () => {
    expect(cn('a', false, null, undefined, 'b')).toBe('a b')
  })
})

describe('formatDate / formatDateTime / formatMonth', () => {
  it('returns dashes for nullish input', () => {
    expect(formatDate(null)).toBe('-')
    expect(formatDateTime(undefined)).toBe('-')
    expect(formatMonth('')).toBe('-')
  })

  it('returns dashes for invalid dates', () => {
    expect(formatDate('not-a-date')).toBe('-')
  })

  it('formats valid ISO dates', () => {
    expect(formatDate('2026-03-14T10:00:00Z')).toBe('14/03/2026')
    expect(formatMonth('2026-03-14T10:00:00Z')).toBe('Mar 2026')
    expect(formatDateTime('2026-03-14T10:30:00Z')).toMatch(/^14\/03\/2026 \d{2}:\d{2}$/)
  })

  // A SQL Server deployment serializes its timestamps without a zone marker. Read as local
  // time they land hours away from the instant the server meant.
  it('reads a timestamp with no zone marker as UTC, like one that carries a Z', () => {
    expect(formatDateTime('2026-03-14T10:30:00')).toBe(formatDateTime('2026-03-14T10:30:00Z'))
    expect(formatDate('2026-03-14T23:30:00.1234567')).toBe(
      formatDate('2026-03-14T23:30:00.1234567Z'),
    )
  })

  it('leaves a stated offset alone', () => {
    expect(formatDateTime('2026-03-14T10:30:00+05:00')).toBe(
      formatDateTime('2026-03-14T05:30:00Z'),
    )
  })
})

describe('formatRelativeToNow', () => {
  it('returns null for nullish or unreadable input', () => {
    expect(formatRelativeToNow(null)).toBeNull()
    expect(formatRelativeToNow('not-a-date')).toBeNull()
  })

  it('places a zone-less server timestamp minutes in the past, not hours in the future', () => {
    const justNow = new Date(Date.now() - 90_000).toISOString().replace('Z', '')

    expect(formatRelativeToNow(justNow)).toMatch(/^\d+ minutes? ago$/)
  })
})

describe('formatDuration', () => {
  it('returns dash for nullish input', () => {
    expect(formatDuration(null)).toBe('-')
    expect(formatDuration(undefined)).toBe('-')
    expect(formatDuration('')).toBe('-')
  })

  it('returns dash for unparseable input', () => {
    expect(formatDuration('not-a-duration')).toBe('-')
    expect(formatDuration('abc:def:ghi')).toBe('-')
  })

  it('formats sub-second durations as ms', () => {
    expect(formatDuration('00:00:00.5000000')).toBe('500ms')
    expect(formatDuration('00:00:00.0250000')).toBe('25ms')
    expect(formatDuration('00:00:00')).toBe('0ms')
  })

  it('formats sub-minute durations as seconds with one decimal', () => {
    expect(formatDuration('00:00:01')).toBe('1s')
    expect(formatDuration('00:00:30')).toBe('30s')
    expect(formatDuration('00:00:45.5000000')).toBe('46s')
  })

  it('formats sub-hour durations as Xm Ys', () => {
    expect(formatDuration('00:01:00')).toBe('1m 0s')
    expect(formatDuration('00:02:30')).toBe('2m 30s')
    expect(formatDuration('00:59:59')).toBe('59m 59s')
  })

  it('formats sub-day durations as Xh Ym', () => {
    expect(formatDuration('01:00:00')).toBe('1h 0m')
    expect(formatDuration('02:30:15')).toBe('2h 30m')
  })

  it('formats multi-day durations as Xd Yh', () => {
    expect(formatDuration('1.00:00:00')).toBe('1d 0h')
    expect(formatDuration('2.05:30:00')).toBe('2d 5h')
  })

  it('rolls over to the next unit when rounding crosses a boundary', () => {
    // 999.6ms would round to "1000ms" — should be "1s" instead.
    expect(formatDuration('00:00:00.9996000')).toBe('1s')
    // 59.5s would round to "60s" — should be "1m 0s" instead.
    expect(formatDuration('00:00:59.9500000')).toBe('1m 0s')
    // 59m 59.5s would round to "59m 60s" — should be "1h 0m".
    expect(formatDuration('00:59:59.5000000')).toBe('1h 0m')
    // 23h 59m 59.5s would round to "23h 60m" — should be "1d 0h".
    expect(formatDuration('23:59:59.5000000')).toBe('1d 0h')
  })
})

describe('formatDurationMs', () => {
  it('clamps negative input to 0', () => {
    expect(formatDurationMs(-1)).toBe('0ms')
    expect(formatDurationMs(-1_000_000)).toBe('0ms')
  })

  it('formats common ranges', () => {
    expect(formatDurationMs(500)).toBe('500ms')
    expect(formatDurationMs(1_000)).toBe('1s')
    expect(formatDurationMs(45_500)).toBe('46s')
    expect(formatDurationMs(150_000)).toBe('2m 30s')
    expect(formatDurationMs(3_600_000)).toBe('1h 0m')
    expect(formatDurationMs(86_400_000)).toBe('1d 0h')
  })

  it('rolls over to the next unit when rounding crosses a boundary', () => {
    expect(formatDurationMs(999.6)).toBe('1s')
    expect(formatDurationMs(59_950)).toBe('1m 0s')
  })
})

describe('getDecodedToken / isTokenValid', () => {
  it('returns null when no token is stored', () => {
    expect(getDecodedToken()).toBeNull()
    expect(isTokenValid()).toBeFalsy()
  })

  it('returns null for malformed tokens', () => {
    localStorage.setItem('accessToken', 'not-a-jwt')
    expect(getDecodedToken()).toBeNull()
  })

  it('decodes a valid token', () => {
    localStorage.setItem('accessToken', makeJwt({ sub: 'user-1', exp: 9999999999 }))
    expect(getDecodedToken()).toMatchObject({ sub: 'user-1' })
    expect(isTokenValid()).toBe(true)
  })

  it('treats expired tokens as invalid', () => {
    localStorage.setItem('accessToken', makeJwt({ sub: 'user-1', exp: 1 }))
    expect(isTokenValid()).toBe(false)
  })
})

describe('hasRole', () => {
  it('returns false without a token', () => {
    expect(hasRole('ADMIN')).toBe(false)
  })

  it('matches a string role', () => {
    localStorage.setItem('accessToken', makeJwt({ role: 'ADMIN', exp: 9999999999 }))
    expect(hasRole('ADMIN')).toBe(true)
    expect(hasRole('SUPER_ADMIN')).toBe(false)
  })

  it('matches a role inside an array', () => {
    localStorage.setItem('accessToken', makeJwt({ role: ['ADMIN', 'EDITOR'], exp: 9999999999 }))
    expect(hasRole('EDITOR')).toBe(true)
    expect(hasRole('VIEWER')).toBe(false)
  })
})

describe('hasMultipleTenants', () => {
  it('is false when token is missing', () => {
    expect(hasMultipleTenants()).toBe(false)
  })

  it('is false with a single tenant', () => {
    localStorage.setItem(
      'accessToken',
      makeJwt({ tenant_ids: JSON.stringify(['t1']), exp: 9999999999 }),
    )
    expect(hasMultipleTenants()).toBe(false)
  })

  it('is true with multiple tenants', () => {
    localStorage.setItem(
      'accessToken',
      makeJwt({ tenant_ids: JSON.stringify(['t1', 't2']), exp: 9999999999 }),
    )
    expect(hasMultipleTenants()).toBe(true)
  })

  it('is false when tenant_ids is not valid JSON', () => {
    localStorage.setItem('accessToken', makeJwt({ tenant_ids: 'garbage', exp: 9999999999 }))
    expect(hasMultipleTenants()).toBe(false)
  })
})

describe('getObservationStatusClass / hasObservationMessages', () => {
  it('prioritizes error over warning', () => {
    expect(
      getObservationStatusClass({ errorMessage: 'boom', warningMessage: 'careful' }),
    ).toBe('status-error')
  })

  it('falls back to warning then ok', () => {
    expect(
      getObservationStatusClass({ errorMessage: null, warningMessage: 'careful' }),
    ).toBe('status-warning')
    expect(
      getObservationStatusClass({ errorMessage: null, warningMessage: null }),
    ).toBe('status-ok')
  })

  it('reads an open warning that needs no review as ok', () => {
    expect(
      getObservationStatusClass({ errorMessage: null, warningMessage: 'note', warningNeedsReview: false }),
    ).toBe('status-ok')
    expect(
      getObservationStatusClass({ errorMessage: null, warningMessage: 'careful', warningNeedsReview: true }),
    ).toBe('status-warning')
  })

  it('hasObservationMessages flags error OR warning', () => {
    expect(
      hasObservationMessages({ errorMessage: 'e', warningMessage: null, clearedWarningMessage: null }),
    ).toBe(true)
    expect(
      hasObservationMessages({ errorMessage: null, warningMessage: 'w', clearedWarningMessage: null }),
    ).toBe(true)
    expect(
      hasObservationMessages({ errorMessage: null, warningMessage: null, clearedWarningMessage: null }),
    ).toBe(false)
  })

  it('hasObservationMessages flags cleared warnings', () => {
    expect(
      hasObservationMessages({ errorMessage: null, warningMessage: null, clearedWarningMessage: 'w' }),
    ).toBe(true)
  })
})

describe('stripXmlTags', () => {
  it('strips simple wrapper tags and keeps inner content', () => {
    expect(stripXmlTags('<answer>hello</answer>')).toBe('hello')
    expect(stripXmlTags('<thinking>plan</thinking><answer>final</answer>')).toBe('planfinal')
  })

  it('keeps prose with stray angle brackets that are not real tags', () => {
    expect(stripXmlTags('x < 5 and y > 3')).toBe('x < 5 and y > 3')
  })

  it('handles attributes inside tags', () => {
    expect(stripXmlTags('<tag attr="value">body</tag>')).toBe('body')
  })

  it('strips tags with trailing whitespace before the closing >', () => {
    // CodeQL js/bad-tag-filter — `<script \n>` should not slip through the filter.
    expect(stripXmlTags('<script >alert(1)</script>')).toBe('alert(1)')
    expect(stripXmlTags('<script\n>alert(1)</script\n>')).toBe('alert(1)')
    expect(stripXmlTags('<a href="x" >body</a >')).toBe('body')
  })

  it('removes residual tags introduced by single-pass replacement', () => {
    // Adversarial input: removing the inner <script>...</script> in a single pass
    // leaves a tag-shaped <scrxipt>...</scrxipt> behind. Looping until stable
    // strips that residue too, so only the inner text remains.
    const input = '<scr<script>x</script>ipt>alert(1)</scr<script>x</script>ipt>'
    expect(stripXmlTags(input)).toBe('alert(1)')
    expect(stripXmlTags(input)).not.toMatch(/<script/i)
  })

  it('returns the empty string unchanged', () => {
    expect(stripXmlTags('')).toBe('')
  })
})

describe('clearSignIn', () => {
  it('forgets the session and the organization chosen in it', () => {
    localStorage.setItem('accessToken', 'access')
    localStorage.setItem('refreshToken', 'refresh')
    localStorage.setItem('expiration', '1')
    localStorage.setItem('selectedOrgId', 'org-of-the-last-user')

    clearSignIn()

    expect(['accessToken', 'refreshToken', 'expiration', 'selectedOrgId'].map((key) => localStorage.getItem(key))).toEqual([null, null, null, null])
  })

  it('clears every session key even when the server does not answer', () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))
    onTestFinished(() => fetchSpy.mockRestore())
    localStorage.setItem('accessToken', 'access')
    localStorage.setItem('refreshToken', 'refresh')
    localStorage.setItem('expiration', '1')
    localStorage.setItem('selectedOrgId', 'org-of-the-last-user')
    localStorage.setItem('theme', 'dark')
    localStorage.setItem('capabilitiesVary', 'true')

    clearSignIn()

    expect(Array.from({ length: localStorage.length }, (_, index) => localStorage.key(index)).sort()).toEqual(['capabilitiesVary', 'theme'])
    expect(fetchSpy).not.toHaveBeenCalled()
  })
})

describe('formatLongDateTime', () => {
  it('writes the day, the month name, the year and the time', () => {
    expect(formatLongDateTime('2026-11-12T17:31:00Z')).toMatch(/^\d{1,2} November 2026, \d{1,2}:\d{2}(am|pm)$/)
  })

  it('writes a dash for a value that is not a date, instead of failing the page', () => {
    expect(formatLongDateTime('not a date')).toBe('-')
    expect(formatLongDateTime(null)).toBe('-')
  })
})
