import type { DataOutputCitation } from '@/api/workflowReports/schema'
import { escapeHtml } from '@/lib/escapeHtml'

const INDENT = '  '

// RFC 6901: "~" is escaped before "/", as the server writes the paths.
const pointerToken = (key: string): string => key.replace(/~/g, '~0').replace(/\//g, '~1')

/**
 * Wraps each cited figure of one value in a fact marker, in the order the server found them, so
 * a figure written twice in one value is marked as many times as it was cited.
 */
const markFigures = (text: string, citations: DataOutputCitation[]): string => {
  let marked = ''
  let cursor = 0
  for (const citation of citations) {
    const start = text.indexOf(citation.value, cursor)
    if (!citation.value || start < 0) {
      continue
    }
    marked += escapeHtml(text.slice(cursor, start))
    marked += `<fact id="${escapeHtml(citation.id)}">${escapeHtml(citation.value)}</fact>`
    cursor = start + citation.value.length
  }
  return marked + escapeHtml(text.slice(cursor))
}

/**
 * The data output's JSON as indented HTML: the text reads exactly as `JSON.stringify(data, null, 2)`,
 * and each cited figure is a `<fact id>` marker, so the report's sources popup opens on it.
 */
export function renderDataOutputJson(data: unknown, citations: DataOutputCitation[]): string {
  const citationsByPath = new Map<string, DataOutputCitation[]>()
  for (const citation of citations) {
    const onPath = citationsByPath.get(citation.path)
    if (onPath) {
      onPath.push(citation)
    } else {
      citationsByPath.set(citation.path, [citation])
    }
  }

  const render = (value: unknown, path: string, indent: string): string => {
    if (Array.isArray(value)) {
      if (value.length === 0) {
        return '[]'
      }
      const inner = indent + INDENT
      const items = value.map((item, index) => inner + render(item, `${path}/${index}`, inner))
      return `[\n${items.join(',\n')}\n${indent}]`
    }

    if (value !== null && typeof value === 'object') {
      const entries = Object.entries(value as Record<string, unknown>)
      if (entries.length === 0) {
        return '{}'
      }
      const inner = indent + INDENT
      const members = entries.map(([key, member]) =>
        `${inner}<span class="json-key">${escapeHtml(JSON.stringify(key))}</span>: `
        + render(member, `${path}/${pointerToken(key)}`, inner))
      return `{\n${members.join(',\n')}\n${indent}}`
    }

    return markFigures(JSON.stringify(value), citationsByPath.get(path) ?? [])
  }

  return render(data, '', '')
}
