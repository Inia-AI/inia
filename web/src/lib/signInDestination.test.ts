import { describe, expect, it } from 'vitest'
import { destinationAfterSignIn } from './signInDestination'

describe('destinationAfterSignIn', () => {
  it('goes Home with no redirect', () => {
    expect(destinationAfterSignIn(undefined)).toEqual({ name: 'Home' })
  })

  it('goes back to the page the user was sent from', () => {
    expect(destinationAfterSignIn('/workflow-reports?page=2')).toBe('/workflow-reports?page=2')
  })

  it.each(['//evil.example/path', '/\\evil.example', '/\t/evil.example', '/\n/evil.example', '/ /evil.example', 'https://evil.example', 'javascript:alert(1)'])(
    'goes Home rather than to %s',
    redirect => {
      expect(destinationAfterSignIn(redirect)).toEqual({ name: 'Home' })
    },
  )
})
