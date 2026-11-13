import type { LocationQueryValue, RouteLocationRaw } from 'vue-router'

const HOME: RouteLocationRaw = { name: 'Home' }

/** A path inside the app: never `//host` or `/\host`, which a browser reads as another site, nor a space or control character a URL parser drops to make one. */
export function inAppPath(redirect: LocationQueryValue | LocationQueryValue[] | undefined): string | null {
  return typeof redirect === 'string' && /^\/(?![/\\])[^\s\x00-\x1f\x7f]*$/.test(redirect) ? redirect : null
}

/** The page the user was sent from, else Home. */
export function destinationAfterSignIn(redirect: LocationQueryValue | LocationQueryValue[] | undefined): RouteLocationRaw {
  return inAppPath(redirect) ?? HOME
}
