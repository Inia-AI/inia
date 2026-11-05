export interface NamedFile {
  name: string
  extension?: string | null
}

/**
 * Mirrors the server's own composition (`Document.FileName` — `Name.TrimEnd().WithExtension(...)`,
 * `IniaAi.Common/Extensions/StringExtensions.cs`). `FileMetadata.Name` is meant to hold the stem,
 * but some write path stores the whole file name in it, so any consumer that concatenates name and
 * extension doubles it — about 6% of stored files.
 */
export function fullFileName(file: NamedFile): string {
  const name = file.name.trimEnd()
  const extension = file.extension ?? ''
  if (extension === '' || name.toLowerCase().endsWith(extension.toLowerCase())) {
    return name
  }
  return `${name}${extension}`
}

export const fileExtension = (fileName: string | undefined): string =>
  fileName?.split('.').pop()?.toLowerCase() ?? ''
