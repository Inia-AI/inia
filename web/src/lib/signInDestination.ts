import type { LocationQueryValue, RouteLocationRaw } from 'vue-router'

export type SignInLanding = () => Promise<RouteLocationRaw | null>

const HOME: RouteLocationRaw = { name: 'Home' }

const LANDING_WAIT_MS = 5000

/** A path inside the app: never `//host` or `/\host`, which a browser reads as another site, nor a space or control character a URL parser drops to make one. */
export function inAppPath(redirect: LocationQueryValue | LocationQueryValue[] | undefined): string | null {
  return typeof redirect === 'string' && /^\/(?![/\\])[^\s\x00-\x1f\x7f]*$/.test(redirect) ? redirect : null
}

async function withinWait(landing: SignInLanding) {
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    return await Promise.race([landing(), new Promise<null>(resolve => (timer = setTimeout(() => resolve(null), LANDING_WAIT_MS)))])
  } finally {
    clearTimeout(timer)
  }
}

/** The first landing a module asks for, else the page the user was sent from, else Home. A landing that fails or takes over five seconds is skipped. */
export async function destinationAfterSignIn(
  landings: readonly SignInLanding[],
  redirect: LocationQueryValue | LocationQueryValue[] | undefined,
): Promise<RouteLocationRaw> {
  for (const landing of landings) {
    const destination = await withinWait(landing).catch(() => null)
    if (destination) {
      return destination
    }
  }
  return inAppPath(redirect) ?? HOME
}
