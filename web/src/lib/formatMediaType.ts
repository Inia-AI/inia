/**
 * Backend MediaType is a long PascalCase enum (e.g. `ApplicationPdf`,
 * `ApplicationVndMsExcelSheetMacroEnabled12`). Nobody wants to see those raw
 * values in a table cell, and nobody wants to pick between dozens of them in a
 * dropdown — so we bucket them into display groups.
 *
 * Every group has:
 *   - `match(raw)`  → is this raw enum value in this group?
 *   - `odata()`     → OData `$filter` fragment that the API understands
 *   - `label`, `icon` → UI display
 *
 * Keep `match()` and `odata()` in lockstep: anything new added to one must be
 * added to the other.
 */

export type MediaTypeGroupId =
  | 'pdf'
  | 'image'
  | 'csv'
  | 'excel'
  | 'word'
  | 'powerpoint'
  | 'text'
  | 'archive'
  | 'audio'
  | 'video'
  | 'email'
  | 'unknown'

interface MediaTypeGroup {
  id: MediaTypeGroupId
  label: string
  icon: string
  match: (raw: string) => boolean
  odata: () => string
}

const ARCHIVE_VALUES = [
  'ApplicationZip',
  'ApplicationRar',
  'Application7z',
  'ApplicationTar',
  'ApplicationGzip',
] as const

const GROUPS: readonly MediaTypeGroup[] = [
  {
    id: 'pdf',
    label: 'PDF',
    icon: 'file-pdf',
    match: r => r === 'ApplicationPdf',
    odata: () => `mediaType eq 'ApplicationPdf'`,
  },
  {
    id: 'image',
    label: 'Image',
    icon: 'file-image',
    match: r => r.startsWith('Image'),
    odata: () => `startswith(mediaType, 'Image')`,
  },
  {
    id: 'csv',
    label: 'CSV',
    icon: 'file-csv',
    match: r => r === 'TextCsv' || r === 'TextTabSeparatedValues',
    odata: () => `(mediaType eq 'TextCsv' or mediaType eq 'TextTabSeparatedValues')`,
  },
  {
    id: 'excel',
    label: 'Excel',
    icon: 'file-excel',
    match: r => r.includes('Excel') || r.includes('Spreadsheet'),
    odata: () => `(contains(mediaType, 'Excel') or contains(mediaType, 'Spreadsheet'))`,
  },
  {
    id: 'word',
    label: 'Word',
    icon: 'file-word',
    match: r =>
      r.includes('MsWord') ||
      r.includes('Wordprocessing') ||
      r.includes('OasisOpendocumentText'),
    odata: () =>
      `(contains(mediaType, 'MsWord') or contains(mediaType, 'Wordprocessing') or contains(mediaType, 'OasisOpendocumentText'))`,
  },
  {
    id: 'powerpoint',
    label: 'PowerPoint',
    icon: 'file-powerpoint',
    match: r => r.includes('Powerpoint') || r.includes('Presentation'),
    odata: () =>
      `(contains(mediaType, 'Powerpoint') or contains(mediaType, 'Presentation'))`,
  },
  {
    id: 'archive',
    label: 'Archive',
    icon: 'file-archive',
    match: r => (ARCHIVE_VALUES as readonly string[]).includes(r),
    odata: () =>
      `(${ARCHIVE_VALUES.map(v => `mediaType eq '${v}'`).join(' or ')})`,
  },
  {
    id: 'audio',
    label: 'Audio',
    icon: 'file',
    match: r => r.startsWith('Audio'),
    odata: () => `startswith(mediaType, 'Audio')`,
  },
  {
    id: 'video',
    label: 'Video',
    icon: 'file',
    match: r => r.startsWith('Video'),
    odata: () => `startswith(mediaType, 'Video')`,
  },
  {
    id: 'email',
    label: 'Email',
    icon: 'envelope',
    match: r => r === 'MessageRfc822' || r === 'ApplicationVndMsOutlook',
    odata: () =>
      `(mediaType eq 'MessageRfc822' or mediaType eq 'ApplicationVndMsOutlook')`,
  },
  {
    id: 'text',
    label: 'Text',
    icon: 'file-lines',
    // Text-ish things that aren't already claimed by CSV. Evaluated after csv
    // in the lookup so TextCsv goes into CSV rather than Text.
    match: r =>
      (r.startsWith('Text') && r !== 'TextCsv' && r !== 'TextTabSeparatedValues') ||
      r === 'ApplicationJson' ||
      r === 'ApplicationXml' ||
      r === 'ApplicationRtf' ||
      r === 'ApplicationJavascript' ||
      r === 'ApplicationXYaml',
    odata: () =>
      `((startswith(mediaType, 'Text') and mediaType ne 'TextCsv' and mediaType ne 'TextTabSeparatedValues') or mediaType eq 'ApplicationJson' or mediaType eq 'ApplicationXml' or mediaType eq 'ApplicationRtf' or mediaType eq 'ApplicationJavascript' or mediaType eq 'ApplicationXYaml')`,
  },
]

/** Groups exposed to the UI (no `unknown`; keep order for the dropdown). */
export const MEDIA_TYPE_GROUPS: readonly { id: MediaTypeGroupId; label: string }[] =
  GROUPS.map(({ id, label }) => ({ id, label }))

export function mediaTypeGroup(raw: string | null | undefined): MediaTypeGroupId {
  if (!raw) return 'unknown'
  for (const g of GROUPS) {
    if (g.match(raw)) return g.id
  }
  return 'unknown'
}

export type PreviewKind = 'pdf' | 'image'

/**
 * Which in-app preview renderer a media type gets, or `null` when it has no
 * usable inline viewer. PDFs embed in an iframe; images render as <img>.
 */
export function previewKindOf(raw: string | null | undefined): PreviewKind | null {
  const group = mediaTypeGroup(raw)
  if (group === 'pdf') {
    return 'pdf'
  }
  if (group === 'image') {
    return 'image'
  }
  return null
}

export function formatMediaType(raw: string | null | undefined): string {
  if (!raw) return '—'
  const id = mediaTypeGroup(raw)
  return GROUPS.find(g => g.id === id)?.label ?? raw
}

export function mediaTypeIconName(raw: string | null | undefined): string {
  if (!raw) return 'file'
  const id = mediaTypeGroup(raw)
  return GROUPS.find(g => g.id === id)?.icon ?? 'file'
}

/**
 * OData `$filter` fragment for a group id, or `null` if unrecognised. Keep in
 * sync with the `match` side so "Excel" always covers the same rows in the UI
 * and on the server.
 */
export function mediaTypeFilterByGroup(id: string): string | null {
  const g = GROUPS.find(x => x.id === id)
  return g ? g.odata() : null
}
