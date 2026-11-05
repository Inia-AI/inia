import { readonly, shallowRef, type Ref } from 'vue'
import { useResizeObserver } from '@vueuse/core'

interface Between {
  available: number
  start: number
  middle: number
  end: number
  clearance: number
}

export const fitsBetween = ({ available, start, middle, end, clearance }: Between) =>
  middle + 2 * (Math.max(start, end) + clearance) <= available

type ElementRef = Readonly<Ref<HTMLElement | null>>

const pixels = (value: string) => parseFloat(value) || 0

export const useFitsBetween = (container: ElementRef, start: ElementRef, middle: ElementRef, end: ElementRef) => {
  const fits = shallowRef(true)
  const measure = () => {
    if (!container.value || !start.value || !middle.value || !end.value) return
    const style = getComputedStyle(container.value)
    fits.value = fitsBetween({
      available: container.value.getBoundingClientRect().width - pixels(style.paddingLeft) - pixels(style.paddingRight),
      start: start.value.getBoundingClientRect().width,
      middle: middle.value.getBoundingClientRect().width,
      end: end.value.getBoundingClientRect().width,
      clearance: pixels(style.columnGap),
    })
  }
  useResizeObserver([container, start, middle, end], measure)
  return readonly(fits)
}
