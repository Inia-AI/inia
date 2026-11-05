export function escapeODataValue(value: string): string {
  return value.replace(/'/g, "''")
}

export function quoteODataLiteral(value: string): string {
  return `'${escapeODataValue(value)}'`
}

export function odataIn(field: string, values: readonly string[]): string {
  if (!values.length) return ''
  return `${field} in (${values.map(quoteODataLiteral).join(',')})`
}

// Case-insensitive contains across several fields. The literal is pre-lowercased once so
// only the field side needs tolower().
export function odataContainsAny(fields: readonly string[], term: string): string {
  const trimmed = term.trim().toLowerCase()
  if (!trimmed) return ''
  const literal = quoteODataLiteral(trimmed)
  return `(${fields.map(field => `contains(tolower(${field}), ${literal})`).join(' or ')})`
}
