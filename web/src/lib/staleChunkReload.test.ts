import { describe, expect, it, vi } from 'vitest'
import { reloadOnStaleChunk, type StaleChunkPage } from './staleChunkReload'

const pageAt = (time: { now: number }) => {
  const page: StaleChunkPage & { events: EventTarget } = {
    events: new EventTarget(),
    addEventListener: (type, listener) => page.events.addEventListener(type, listener),
    sessionStorage: window.sessionStorage,
    location: { reload: vi.fn() },
  }
  reloadOnStaleChunk(page, () => time.now)
  return page
}

const failChunk = (page: { events: EventTarget }) => {
  const event = new Event('vite:preloadError', { cancelable: true })
  page.events.dispatchEvent(event)
  return event
}

describe('reloadOnStaleChunk', () => {
  it('reloads the page when a chunk of an older deploy fails to load, and keeps the error from throwing', () => {
    window.sessionStorage.clear()
    const page = pageAt({ now: 100_000 })

    const event = failChunk(page)

    expect(page.location.reload).toHaveBeenCalledTimes(1)
    expect(event.defaultPrevented).toBe(true)
  })

  it('lets the error through when the reload it just made did not help, so a missing chunk cannot loop', () => {
    window.sessionStorage.clear()
    const time = { now: 100_000 }
    failChunk(pageAt(time))

    time.now += 3_000
    const reloaded = pageAt(time)
    const event = failChunk(reloaded)

    expect(reloaded.location.reload).not.toHaveBeenCalled()
    expect(event.defaultPrevented).toBe(false)
  })

  it('reloads again for a later deploy', () => {
    window.sessionStorage.clear()
    const time = { now: 100_000 }
    failChunk(pageAt(time))

    time.now += 60 * 60_000
    const later = pageAt(time)
    failChunk(later)

    expect(later.location.reload).toHaveBeenCalledTimes(1)
  })
})
