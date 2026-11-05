import { ref, watch, type Ref } from 'vue'

/**
 * Sanitizes a user-entered reference ID: uppercase, alphanumeric + hyphens/underscores only.
 */
export function sanitizeId(value: string): string {
  return value.toUpperCase().replace(/[^A-Z0-9_-]/g, '')
}

/**
 * Generates a slug from a counterparty name for use as a default reference ID.
 * e.g., "Goldman Sachs" → "GOLDMAN-SACHS", "Boeing Corp." → "BOEING-CORP"
 */
export function generateSlug(name: string): string {
  return name
    .trim()
    .toUpperCase()
    .replace(/[^A-Z0-9\s_-]/g, '') // remove special chars except spaces, underscores and hyphens
    .replace(/\s+/g, '-') // spaces → hyphens
    .replace(/-+/g, '-') // collapse multiple hyphens
    .replace(/^-|-$/g, '') // trim leading/trailing hyphens
}

/**
 * Composable for auto-generating a reference ID from a name field.
 * Stops auto-syncing once the user manually edits the ID.
 */
export function useReferenceId(nameSource: Ref<string>, initialId?: string) {
  const referenceId = ref(initialId ?? '')
  const isManuallyEdited = ref(!!initialId)

  watch(nameSource, newName => {
    if (!isManuallyEdited.value) {
      referenceId.value = generateSlug(newName)
    }
  })

  function onManualInput(value: string) {
    isManuallyEdited.value = true
    referenceId.value = sanitizeId(value)
  }

  function reset(name: string, id?: string) {
    if (id) {
      referenceId.value = id
      isManuallyEdited.value = true
    } else {
      referenceId.value = generateSlug(name)
      isManuallyEdited.value = false
    }
  }

  return {
    referenceId,
    isManuallyEdited,
    onManualInput,
    reset,
  }
}
