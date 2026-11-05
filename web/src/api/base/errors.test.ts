import { describe, expect, it } from 'vitest'
import { problemDetailsMessage } from './errors'

describe('problemDetailsMessage', () => {
  it('shows the reason of a refusal for a missing capability, not its title', () => {
    expect(
      problemDetailsMessage({
        title: 'Not included',
        detail: 'Your account has no active subscription, so new reports cannot start. Subscribe to run reports.',
        capability: 'upload',
      }),
    ).toBe('Your account has no active subscription, so new reports cannot start. Subscribe to run reports.')
  })

  it('keeps a specific title for any other refusal', () => {
    expect(problemDetailsMessage({ title: 'File too large', detail: 'Ignored detail' })).toBe('File too large')
  })
})
