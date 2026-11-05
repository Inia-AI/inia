import { ref, watch, type Ref } from 'vue'

/**
 * Creates a debounced version of a value
 * @param value - The ref to debounce
 * @param delay - Delay in milliseconds (default: 300ms)
 * @returns Debounced ref
 */
export function useDebouncedRef<T>(value: Ref<T>, delay = 300): Ref<T> {
  const debouncedValue = ref(value.value) as Ref<T>
  let timeout: ReturnType<typeof setTimeout> | null = null

  watch(value, (newValue) => {
    if (timeout) {
      clearTimeout(timeout)
    }

    timeout = setTimeout(() => {
      debouncedValue.value = newValue
      timeout = null
    }, delay)
  })

  return debouncedValue
}

/**
 * Creates a debounced function
 * @param fn - Function to debounce
 * @param delay - Delay in milliseconds (default: 300ms)
 * @returns Debounced function
 */
export function useDebounce<T extends (...args: any[]) => any>(
  fn: T,
  delay = 300
): (...args: Parameters<T>) => void {
  let timeout: ReturnType<typeof setTimeout> | null = null

  return (...args: Parameters<T>) => {
    if (timeout) {
      clearTimeout(timeout)
    }

    timeout = setTimeout(() => {
      fn(...args)
      timeout = null
    }, delay)
  }
}

/**
 * Creates a debounced emit function for Vue components
 * Useful for debouncing user input events
 * @param emitFn - The emit function from the component
 * @param delay - Delay in milliseconds (default: 300ms)
 * @returns Debounced emit function
 */
export function useDebouncedEmit<T extends (...args: any[]) => any>(
  emitFn: T,
  delay = 300
) {
  return useDebounce(emitFn, delay)
}
