import { z } from 'zod'

export interface FileDto {
  id: string
  filename: string
  mediaType: string
  created: boolean
  producesPages: boolean
  warnings?: string[]
}

export interface UploadFileMutationVariables {
  formData: FormData
  onUploadProcessChange: (percent: number) => void
}

export const FILE_UPLOAD_ERROR_CODES = {
  passwordRequired: 'File.Password.Required',
  passwordIncorrect: 'File.Password.Incorrect',
  passwordUnsupported: 'File.Password.Unsupported',
  refusedPrefix: 'File.Refused.',
  storageFull: 'File.Refused.StorageFull',
  tooManyUploads: 'File.Refused.TooManyUploads',
} as const

/** The body of an upload refused because the organization's storage is full (403). */
export const StorageFullRefusal = z.object({
  title: z.string(),
  code: z.literal(FILE_UPLOAD_ERROR_CODES.storageFull),
})

/** The body of an upload refused because the organization uploaded the most files allowed in an hour (429). */
export const TooManyUploadsRefusal = z.object({
  title: z.string(),
  code: z.literal(FILE_UPLOAD_ERROR_CODES.tooManyUploads),
  resetsAt: z.iso.datetime(),
})
