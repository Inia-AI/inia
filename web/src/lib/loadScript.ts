const scriptLoadCache = new Map<string, Promise<void>>()

/** Adds the script at `src` to the page once; a failed load is forgotten, so the next call tries again. */
export const loadScript = (src: string, checkLoaded: () => boolean): Promise<void> => {
  if (checkLoaded()) {
    return Promise.resolve()
  }

  const existing = scriptLoadCache.get(src)
  if (existing) {
    return existing
  }

  const promise = new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = src
    script.async = true
    script.defer = true
    script.onload = () => resolve()
    script.onerror = () => {
      scriptLoadCache.delete(src)
      reject(new Error(`Failed to load script: ${src}`))
    }
    document.head.appendChild(script)
  })

  scriptLoadCache.set(src, promise)
  return promise
}
