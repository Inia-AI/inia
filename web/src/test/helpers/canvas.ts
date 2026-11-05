import { vi } from 'vitest'

/** A 2D canvas context that accepts every call and measures text by its length. */
function canvasContext(): CanvasRenderingContext2D {
  const accept: ProxyHandler<object> = {
    get: (target, key) => (key === 'measureText' ? (text: string) => ({ width: text.length * 6 }) : (Reflect.get(target, key) ?? context)),
    apply: () => context
  }
  const context = new Proxy(function () {}, accept) as unknown as CanvasRenderingContext2D
  return context
}

const CHART_WIDTH = 600
const CHART_HEIGHT = 300

/**
 * jsdom has no canvas and lays nothing out, and a chart draws on a canvas the size of its element. Gives every canvas
 * a context that draws nothing, and every element a size.
 */
export const standInCanvas = () => {
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(() => canvasContext() as never)
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(CHART_WIDTH)
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(CHART_HEIGHT)
}
