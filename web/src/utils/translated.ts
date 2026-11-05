/**
 * The message for `key`, or null when the locale has none: vue-i18n answers a missing key with
 * the key itself, which must never reach the screen.
 */
export function translated(t: (key: string) => string, key: string): string | null {
  const message = t(key)
  return message === key ? null : message
}
