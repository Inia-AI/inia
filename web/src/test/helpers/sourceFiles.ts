const RAW_SOURCES = import.meta.glob<string>(
  ['/src/**/*.vue', '/src/**/*.ts', '/src/**/*.css', '!/src/**/*.test.ts', '!/src/**/*.d.ts'],
  { query: '?raw', import: 'default', eager: true },
)

const SOURCE_PREFIX = '/src/'

export const SOURCES: ReadonlyMap<string, string> = new Map(
  Object.entries(RAW_SOURCES).map(([path, text]) => [path.slice(SOURCE_PREFIX.length), text]),
)

export const sourcePaths = (pattern: RegExp) => [...SOURCES.keys()].filter(path => pattern.test(path)).sort()

export const sourceOf = (path: string) => SOURCES.get(path) ?? ''

export const directoryOf = (path: string) => path.slice(0, Math.max(0, path.lastIndexOf('/')))

export const joinPath = (...parts: string[]) => {
  const segments: string[] = []
  for (const segment of parts.join('/').split('/')) {
    if (!segment || segment === '.') continue
    if (segment === '..') segments.pop()
    else segments.push(segment)
  }
  return segments.join('/')
}
