/**
 * Content rules for counterparty names and name variants, mirroring the API-side
 * CounterpartyNameRules (IniaAi.Domain). Names are interpolated into workflow prompts
 * and raw JSON payloads, so characters that can break JSON strings or template
 * placeholders are rejected. Apostrophes are allowed (e.g. O'Reilly). Pipes are allowed
 * in name variants only, where they separate variants (e.g. "ibm corp|int.bus.m").
 */
export const FORBIDDEN_NAME_CHARACTERS = '"\u201C\u201D\u201E\u00AB\u00BB`\\{}|<>'

// Mirrors the Name column's [StringLength] on the API side.
export const COUNTERPARTY_NAME_MAX_LENGTH = 250

export const FORBIDDEN_NAME_VARIANTS_CHARACTERS = '"\u201C\u201D\u201E\u00AB\u00BB`\\{}<>'

function containsForbidden(value: string, forbidden: string): boolean {
  for (const char of value) {
    if (forbidden.includes(char)) return true
    const code = char.codePointAt(0) ?? 0
    const isControl = code < 0x20 || (code >= 0x7f && code <= 0x9f)
    if (isControl) return true
  }
  return false
}

export function containsForbiddenNameCharacters(value: string): boolean {
  return containsForbidden(value, FORBIDDEN_NAME_CHARACTERS)
}

export function isValidCounterpartyName(value: string | null | undefined): boolean {
  if (value == null || value === '') return true
  return !containsForbidden(value, FORBIDDEN_NAME_CHARACTERS)
}

export function isValidCounterpartyNameVariants(value: string | null | undefined): boolean {
  if (value == null || value === '') return true
  return !containsForbidden(value, FORBIDDEN_NAME_VARIANTS_CHARACTERS)
}
