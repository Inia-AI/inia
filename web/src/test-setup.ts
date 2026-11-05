import { afterEach, beforeEach } from 'vitest'
import { enableAutoUnmount } from '@vue/test-utils'

// A view a test leaves mounted keeps watching shared state, so it recomputes whenever a later
// test in the same file changes that state: the file slows down test by test.
enableAutoUnmount(afterEach)

// Under Vitest 4 + jsdom 29 in this repo, the `localStorage` exposed on the
// test global is missing its Storage prototype methods (`getItem`, `setItem`,
// `clear`, etc.) — calls throw "is not a function". A direct `new JSDOM()`
// instantiation does not reproduce this, so the cause is specific to the
// Vitest/jsdom integration, not jsdom itself. Rather than pin versions or
// chase the upstream bug, we install a Map-backed Web Storage polyfill here
// and make the tests independent of whatever the environment exposes.
// Wired up via `setupFiles` in vite.config.ts. Don't remove without
// re-verifying that `localStorage.clear()` works in a jsdom test.
class MapStorage implements Storage {
  private store = new Map<string, string>()

  get length(): number {
    return this.store.size
  }

  clear(): void {
    this.store.clear()
  }

  getItem(key: string): string | null {
    return this.store.has(key) ? (this.store.get(key) ?? null) : null
  }

  key(index: number): string | null {
    return Array.from(this.store.keys())[index] ?? null
  }

  removeItem(key: string): void {
    this.store.delete(key)
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value))
  }
}

const installStorage = () => {
  const local = new MapStorage()
  const session = new MapStorage()
  Object.defineProperty(globalThis, 'localStorage', {
    value: local,
    configurable: true,
    writable: true,
  })
  Object.defineProperty(globalThis, 'sessionStorage', {
    value: session,
    configurable: true,
    writable: true,
  })
  if (typeof window !== 'undefined') {
    Object.defineProperty(window, 'localStorage', {
      value: local,
      configurable: true,
      writable: true,
    })
    Object.defineProperty(window, 'sessionStorage', {
      value: session,
      configurable: true,
      writable: true,
    })
  }
}

installStorage()

beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
})

// jsdom implements no scrolling, so a component that scrolls a located element into view would
// throw rather than be exercised.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {}
}

// jsdom gives a Range no rects, so a rich-text editor that scrolls its caret into view on focus
// would throw rather than be exercised.
if (!Range.prototype.getClientRects) {
  Range.prototype.getClientRects = () => document.body.getClientRects()
}
if (!Range.prototype.getBoundingClientRect) {
  Range.prototype.getBoundingClientRect = () => document.body.getBoundingClientRect()
}
