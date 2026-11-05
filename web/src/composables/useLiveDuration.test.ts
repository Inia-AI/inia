process.env.TZ = 'Asia/Bangkok'

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, ref } from 'vue'

import { WorkflowState } from '@/enums'
import { useLiveDuration } from './useLiveDuration'

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-05-03T10:00:30.000Z'))
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useLiveDuration', () => {
  it('returns the formatted server duration once the run is terminal', () => {
    const source = ref({
      duration: '00:01:30',
      startedAt: '2026-05-03T09:59:00.000Z',
      workflowState: WorkflowState.Draft,
    })
    const scope = effectScope()
    let durationRef!: ReturnType<typeof useLiveDuration>
    scope.run(() => {
      durationRef = useLiveDuration(source)
    })
    expect(durationRef.value).toBe('1m 30s')
    scope.stop()
  })

  it('returns null when neither startedAt nor server duration is available', () => {
    const source = ref({
      workflowState: WorkflowState.Queued,
    })
    const scope = effectScope()
    let durationRef!: ReturnType<typeof useLiveDuration>
    scope.run(() => {
      durationRef = useLiveDuration(source)
    })
    expect(durationRef.value).toBeNull()
    scope.stop()
  })

  it('reads a startedAt with no zone marker as UTC, so the elapsed time is not the viewer offset', () => {
    const source = ref({
      duration: null,
      startedAt: '2026-05-03T10:00:00.000',
      workflowState: WorkflowState.Running,
    })
    const scope = effectScope()
    let durationRef!: ReturnType<typeof useLiveDuration>
    scope.run(() => {
      durationRef = useLiveDuration(source)
    })
    expect(durationRef.value).toBe('30s')
    scope.stop()
  })

  it('ticks live elapsed time while the run is in-flight', async () => {
    const source = ref({
      duration: null,
      startedAt: '2026-05-03T10:00:00.000Z', // 30s before "now"
      workflowState: WorkflowState.Running,
    })

    const scope = effectScope()
    let durationRef!: ReturnType<typeof useLiveDuration>
    scope.run(() => {
      durationRef = useLiveDuration(source)
    })

    expect(durationRef.value).toBe('30s')

    // advanceTimersByTime advances Date.now() too — 30s elapses, interval
    // fires up to and including 10:01:00, recomputed elapsed = 60s.
    await vi.advanceTimersByTimeAsync(30_000)
    expect(durationRef.value).toBe('1m 0s')

    scope.stop()
  })

  it('stops ticking and switches to the server duration when state turns terminal', async () => {
    const source = ref({
      duration: null,
      startedAt: '2026-05-03T10:00:00.000Z',
      workflowState: WorkflowState.Running,
    })

    const scope = effectScope()
    let durationRef!: ReturnType<typeof useLiveDuration>
    scope.run(() => {
      durationRef = useLiveDuration(source)
    })

    expect(durationRef.value).toBe('30s')

    source.value = {
      duration: '00:00:42',
      startedAt: '2026-05-03T10:00:00.000Z',
      workflowState: WorkflowState.Draft,
    }
    await vi.advanceTimersByTimeAsync(0)
    expect(durationRef.value).toBe('42s')

    // Advancing further must not change the value — the ticker should be off.
    await vi.advanceTimersByTimeAsync(30_000)
    expect(durationRef.value).toBe('42s')

    scope.stop()
  })

  it('clamps clock skew (startedAt in the future) to 0', () => {
    const source = ref({
      duration: null,
      startedAt: '2026-05-03T10:01:00.000Z', // 30s after "now"
      workflowState: WorkflowState.Running,
    })
    const scope = effectScope()
    let durationRef!: ReturnType<typeof useLiveDuration>
    scope.run(() => {
      durationRef = useLiveDuration(source)
    })
    expect(durationRef.value).toBe('0ms')
    scope.stop()
  })

  it('cleans up its interval when the scope is stopped', () => {
    const source = ref({
      duration: null,
      startedAt: '2026-05-03T10:00:00.000Z',
      workflowState: WorkflowState.Running,
    })

    const scope = effectScope()
    scope.run(() => {
      useLiveDuration(source)
    })

    const before = vi.getTimerCount()
    scope.stop()
    expect(vi.getTimerCount()).toBeLessThan(before)
  })
})
