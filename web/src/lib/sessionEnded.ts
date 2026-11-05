import { clearSignIn } from '@/lib/utils'

export const SESSION_ENDED_REASON = 'session-ended'

// Requests sent before the sign-out are refused after it; their 401s must not replace the reason.
const LATE_REFUSALS_MS = 10_000
let endedAt = Number.NEGATIVE_INFINITY

/** Whether this tab ended its session a moment ago, so a later refusal belongs to that session. */
export const sessionJustEnded = () => Date.now() - endedAt < LATE_REFUSALS_MS

/** Signs the browser out and opens the sign-in screen, which says the session has ended and returns to this page after. */
export async function signInAgain() {
  endedAt = Date.now()
  clearSignIn()
  // Lazy import router to avoid circular dependency
  const { default: router } = await import('@/plugins/router')
  const here = router.currentRoute.value
  await router.push({ name: 'Login', query: { reason: SESSION_ENDED_REASON, redirect: here.meta.isPublic ? undefined : here.fullPath } })
}
