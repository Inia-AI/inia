/**
 * Workflow variable tags indexed by their lowercase form.
 *
 * The platform resolves a submitted variable tag against the template's tags without regard to
 * case, and refuses a payload holding two tags that differ only in case. A lowercase form claimed
 * by two differently cased tags therefore maps to `null`: no consumer may guess between them.
 */
export function tagsByLowercase(tags: readonly string[]): Map<string, string | null> {
  const byLowercase = new Map<string, string | null>()
  for (const tag of tags) {
    const lowercase = tag.toLowerCase()
    const claimed = byLowercase.get(lowercase)
    if (claimed === undefined) {
      byLowercase.set(lowercase, tag)
    } else if (claimed !== tag) {
      byLowercase.set(lowercase, null)
    }
  }
  return byLowercase
}

/** Stored keys grouped by their lowercase form, each group in the order the keys were stored. */
export function groupKeysByLowercase(keys: readonly string[]): Map<string, string[]> {
  const groups = new Map<string, string[]>()
  for (const key of keys) {
    const lowercase = key.toLowerCase()
    const group = groups.get(lowercase)
    if (group === undefined) {
      groups.set(lowercase, [key])
    } else {
      group.push(key)
    }
  }
  return groups
}
