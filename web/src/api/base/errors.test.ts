import { describe, expect, it } from 'vitest'
import { problemDetailsMessage } from './errors'

describe('problemDetailsMessage', () => {
  it('shows the reason of a refusal for a missing capability, not its title', () => {
    expect(
      problemDetailsMessage({
        title: 'Not included',
        detail: 'Uploads are turned off for this account, so new files cannot be added.',
        capability: 'upload',
      }),
    ).toBe('Uploads are turned off for this account, so new files cannot be added.')
  })

  it('keeps a specific title for any other refusal', () => {
    expect(problemDetailsMessage({ title: 'File too large', detail: 'Ignored detail' })).toBe('File too large')
  })
})
