/**
 * Utilities for extracting values from raw block responses using property paths.
 * Supports XML tag extraction and JSON field access.
 */

/**
 * Extracts a value from a raw response using property path.
 * Supports XML tag extraction and JSON field access.
 *
 * @param rawResponse - The raw response string from a block execution
 * @param propertyPath - The property path to extract (e.g., "rawValue", "net_working_capital", "Json.field.nested")
 * @returns The extracted value or null if not found
 *
 * @example
 * // XML tag extraction
 * extractValueFromResponse('<rawValue>0.156372</rawValue>', 'rawValue')
 * // Returns: '0.156372'
 *
 * @example
 * // JSON field extraction
 * extractValueFromResponse('{"name": "test", "value": 123}', 'Json.name')
 * // Returns: 'test'
 *
 * @example
 * // Direct JSON field (without Json. prefix)
 * extractValueFromResponse('{"name": "test"}', 'name')
 * // Returns: 'test' (if response is valid JSON)
 */
export function extractValueFromResponse(rawResponse: string, propertyPath: string): string | null {
  if (!rawResponse || !propertyPath) return null

  const trimmedResponse = rawResponse.trim()

  // Handle "Json." prefix for explicit JSON access
  if (propertyPath.startsWith('Json.')) {
    const jsonPath = propertyPath.substring(5) // Remove "Json." prefix
    return extractJsonValue(trimmedResponse, jsonPath)
  }

  // Try JSON extraction first if response looks like JSON
  if (trimmedResponse.startsWith('{') || trimmedResponse.startsWith('[')) {
    try {
      const json = JSON.parse(trimmedResponse)
      const value = getNestedValue(json, propertyPath)
      if (value !== undefined && value !== null) {
        return typeof value === 'object' ? JSON.stringify(value) : String(value)
      }
    } catch {
      // Not valid JSON, continue to XML extraction
    }
  }

  // XML tag extraction: <tagName>value</tagName>
  const tagRegex = new RegExp(`<${escapeRegExp(propertyPath)}>([\\s\\S]*?)</${escapeRegExp(propertyPath)}>`, 'i')
  const match = trimmedResponse.match(tagRegex)
  if (match && match[1] !== undefined) {
    return match[1].trim()
  }

  return null
}

/**
 * Extracts a JSON value from a response string.
 * The response may contain a JSON block wrapped in XML tags or be raw JSON.
 *
 * @param rawResponse - The raw response that may contain JSON
 * @param jsonPath - Dot-separated path to the value (e.g., "field.nested.value")
 * @returns The extracted value or null if not found
 */
function extractJsonValue(rawResponse: string, jsonPath: string): string | null {
  let jsonString = rawResponse.trim()

  // Check if JSON is wrapped in XML tags (e.g., <jsonResponse>{...}</jsonResponse>)
  const jsonTagMatch = jsonString.match(/<jsonResponse>([\s\S]*?)<\/jsonResponse>/i)
  if (jsonTagMatch && jsonTagMatch[1] !== undefined) {
    jsonString = jsonTagMatch[1].trim()
  }

  try {
    const json = JSON.parse(jsonString)
    const value = getNestedValue(json, jsonPath)
    if (value !== undefined && value !== null) {
      return typeof value === 'object' ? JSON.stringify(value) : String(value)
    }
  } catch {
    // Not valid JSON
  }

  return null
}

/**
 * Gets a nested value from an object using a dot-separated path.
 *
 * @param obj - The object to traverse
 * @param path - Dot-separated path (e.g., "field.nested.value")
 * @returns The value at the path or undefined if not found
 */
function getNestedValue(obj: unknown, path: string): unknown {
  if (obj === null || obj === undefined) return undefined
  if (!path) return obj

  return path.split('.').reduce((current: unknown, key: string) => {
    if (current === null || current === undefined) return undefined
    if (typeof current !== 'object') return undefined
    return (current as Record<string, unknown>)[key]
  }, obj)
}

/**
 * Escapes special regex characters in a string.
 */
function escapeRegExp(string: string): string {
  return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * Parses a full placeholder like "FX_DKK.rawValue" into base tag and property path.
 *
 * @param placeholder - The full placeholder string
 * @returns Object with baseTag and propertyPath (propertyPath is null if no dot notation)
 */
export function parsePlaceholder(placeholder: string): { baseTag: string; propertyPath: string | null } {
  const dotIndex = placeholder.indexOf('.')
  if (dotIndex > 0) {
    return {
      baseTag: placeholder.substring(0, dotIndex),
      propertyPath: placeholder.substring(dotIndex + 1)
    }
  }
  return {
    baseTag: placeholder,
    propertyPath: null
  }
}
