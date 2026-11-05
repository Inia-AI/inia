const HTML_ESCAPES: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
}

/** Escapes text for HTML content and quoted attribute values alike. */
export const escapeHtml = (text: string): string => text.replace(/[&<>"']/g, char => HTML_ESCAPES[char] ?? char)
