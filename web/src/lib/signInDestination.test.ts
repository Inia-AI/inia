import { afterEach, describe, expect, it, vi } from 'vitest'
import { destinationAfterSignIn } from './signInDestination'

const ELSEWHERE = { name: 'Elsewhere' }

afterEach(() => {
  vi.useRealTimers()
})

describe('destinationAfterSignIn', () => {
  it('goes Home with no landing and no redirect', async () => {
    expect(await destinationAfterSignIn([], undefined)).toEqual({ name: 'Home' })
  })

  it('goes back to the page the user was sent from', async () => {
    expect(await destinationAfterSignIn([], '/workflow-reports?page=2')).toBe('/workflow-reports?page=2')
  })

  it.each(['//evil.example/path', '/\\evil.example', '/\t/evil.example', '/\n/evil.example', '/ /evil.example', 'https://evil.example', 'javascript:alert(1)'])(
    'goes Home rather than to %s',
    async redirect => {
      expect(await destinationAfterSignIn([], redirect)).toEqual({ name: 'Home' })
    },
  )

  it('goes where a landing asks, over the redirect', async () => {
    expect(await destinationAfterSignIn([async () => ELSEWHERE], '/reports')).toEqual(ELSEWHERE)
  })

  it('keeps the redirect when the landing has no place to send the user', async () => {
    expect(await destinationAfterSignIn([async () => null], '/reports')).toBe('/reports')
  })

  it('skips a landing that fails', async () => {
    const failing = async () => {
      throw new Error('the API did not answer')
    }

    expect(await destinationAfterSignIn([failing], undefined)).toEqual({ name: 'Home' })
  })
  it('stops waiting for a landing after five seconds', async () => {
    vi.useFakeTimers()
    const never = () => new Promise<null>(() => {})

    const destination = destinationAfterSignIn([never], '/reports')
    await vi.advanceTimersByTimeAsync(5000)

    expect(await destination).toBe('/reports')
  })
})
