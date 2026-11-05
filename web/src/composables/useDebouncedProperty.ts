import { ref, watch, type Ref } from 'vue'

/**
 * Creates a debounced local state that syncs to a prop value with a delay.
 * This prevents cursor position loss during typing by avoiding immediate reactivity.
 *
 * @param propValue - The reactive prop value to sync with
 * @param onUpdate - Callback to update the store (called after debounce delay)
 * @param debounceMs - Debounce delay in milliseconds (default: 300ms)
 */
export function useDebouncedProperty<T>(
  propValue: Ref<T>,
  onUpdate: (value: T) => void,
  debounceMs = 300
) {
  // Local state that user types into
  const localValue = ref<T>(propValue.value) as Ref<T>
  let debounceTimer: ReturnType<typeof setTimeout> | null = null

  // Watch prop changes from outside (e.g., undo/redo) and update local value
  watch(propValue, (newValue) => {
    // Only update if different to avoid overwriting user input
    if (localValue.value !== newValue) {
      localValue.value = newValue
    }
  })

  // Watch local value changes and debounce updates to store
  watch(localValue, (newValue) => {
    // Clear existing timer
    if (debounceTimer) {
      clearTimeout(debounceTimer)
    }

    // Set new timer to update store after debounce delay
    debounceTimer = setTimeout(() => {
      onUpdate(newValue)
      debounceTimer = null
    }, debounceMs)
  })

  return localValue
}
