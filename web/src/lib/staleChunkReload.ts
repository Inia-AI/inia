const RELOADED_AT_KEY = 'staleChunkReloadedAt'
const RELOAD_HELPED_WITHIN_MS = 10_000

export interface StaleChunkPage {
  addEventListener: (type: string, listener: (event: Event) => void) => void
  sessionStorage: Pick<Storage, 'getItem' | 'setItem'>
  location: Pick<Location, 'reload'>
}

// A deploy removes the chunks of the build before it, so a tab opened earlier fails to load its
// next screen. Vite reports that as vite:preloadError, and its build guide suggests a reload. A
// second failure right after a reload means the chunk is missing from this deploy too: the error
// then goes through, so the page cannot reload in a loop.
export function reloadOnStaleChunk(page: StaleChunkPage = window, now = () => Date.now()) {
  page.addEventListener('vite:preloadError', event => {
    const reloadedAt = Number(page.sessionStorage.getItem(RELOADED_AT_KEY) ?? 0)
    if (now() - reloadedAt < RELOAD_HELPED_WITHIN_MS) {
      return
    }
    page.sessionStorage.setItem(RELOADED_AT_KEY, String(now()))
    event.preventDefault()
    page.location.reload()
  })
}
