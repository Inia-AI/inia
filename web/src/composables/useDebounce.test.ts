import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

import { useDebounce, useDebouncedEmit, useDebouncedRef } from './useDebounce'

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useDebounce', () => {
  it('coalesces rapid calls into a single invocation', () => {
    const fn = vi.fn()
    const debounced = useDebounce(fn, 100)

    debounced('a')
    debounced('b')
    debounced('c')
    expect(fn).not.toHaveBeenCalled()

    vi.advanceTimersByTime(100)
    expect(fn).toHaveBeenCalledTimes(1)
    expect(fn).toHaveBeenCalledWith('c')
  })

  it('resets the timer on each call', () => {
    const fn = vi.fn()
    const debounced = useDebounce(fn, 100)

    debounced('a')
    vi.advanceTimersByTime(80)
    debounced('b')
    vi.advanceTimersByTime(80)
    expect(fn).not.toHaveBeenCalled()

    vi.advanceTimersByTime(20)
    expect(fn).toHaveBeenCalledWith('b')
  })
})

describe('useDebouncedRef', () => {
  it('mirrors the source ref after the delay', async () => {
    const source = ref('initial')
    const debounced = useDebouncedRef(source, 50)

    expect(debounced.value).toBe('initial')

    source.value = 'updated'
    await vi.advanceTimersByTimeAsync(0)
    expect(debounced.value).toBe('initial')

    await vi.advanceTimersByTimeAsync(50)
    expect(debounced.value).toBe('updated')
  })
})

describe('useDebouncedEmit', () => {
  it('is a thin wrapper over useDebounce', () => {
    const emit = vi.fn()
    const debouncedEmit = useDebouncedEmit(emit, 50)

    debouncedEmit('change', 'x')
    debouncedEmit('change', 'y')
    vi.advanceTimersByTime(50)

    expect(emit).toHaveBeenCalledTimes(1)
    expect(emit).toHaveBeenCalledWith('change', 'y')
  })
})
