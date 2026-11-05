import { describe, it, expect, vi, beforeEach } from 'vitest'
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { axiosError } from '@/test/helpers/axiosError'
import { EN } from '@/test/helpers/messages'

const { uploadMutateMock } = vi.hoisted(() => ({
  uploadMutateMock: vi.fn(),
}))

vi.mock('@/api', () => ({
  default: {
    Files: {
      useUploadDocument: () => ({ mutateAsync: uploadMutateMock }),
    },
  },
}))

import { useDocumentUpload } from './useDocumentUpload'

interface CompApi {
  api: ReturnType<typeof useDocumentUpload>
  unmount: () => void
}

function setup(): CompApi {
  let api: ReturnType<typeof useDocumentUpload> | null = null
  const Comp = defineComponent({
    setup() {
      api = useDocumentUpload()
      return () => h('div')
    },
  })
  const wrapper = mount(Comp, { global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN } })] } })
  return { api: api!, unmount: () => wrapper.unmount() }
}

function makeFile(name: string, content = 'hello'): File {
  return new File([content], name, { type: 'text/plain' })
}

function makeAxiosNoResponseError(): AxiosError {
  return new AxiosError(
    'Network Error',
    AxiosError.ERR_NETWORK,
    { headers: new AxiosHeaders() },
    {},
  )
}

function makeAxiosBadRequest(errors: Record<string, string[]>): AxiosError {
  const response: AxiosResponse = {
    data: {
      status: 400,
      title: 'One or more validation errors occurred.',
      errors,
    },
    status: 400,
    statusText: 'Bad Request',
    headers: {},
    config: { headers: new AxiosHeaders() },
  }
  const error = new AxiosError(
    'Request failed with status code 400',
    AxiosError.ERR_BAD_REQUEST,
    response.config,
    null,
    response,
  )
  return error
}

describe('useDocumentUpload', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('captures backend warnings on a successful upload', async () => {
    uploadMutateMock.mockResolvedValue({
      id: 'doc-1',
      filename: 'email.msg.pdf',
      mediaType: 'application/pdf',
      warnings: ['Email had 2 attachment(s); upload them separately if you need them processed.'],
    })
    const { api, unmount } = setup()

    await api.addFiles([makeFile('email.msg')])

    expect(api.documents.value).toHaveLength(1)
    const doc = api.documents.value[0]
    expect(doc.id).toBe('doc-1')
    expect(doc.warnings).toEqual([
      'Email had 2 attachment(s); upload them separately if you need them processed.',
    ])
    expect(doc.error).toBeUndefined()
    unmount()
  })

  it('flags blockedBeforeServer when the request gets no response at all', async () => {
    uploadMutateMock.mockRejectedValue(makeAxiosNoResponseError())
    const { api, unmount } = setup()

    await api.addFiles([makeFile('report.pdf')])

    const doc = api.documents.value[0]
    expect(doc.error).toBe('blockedBeforeServer')
    expect(doc.id).toBeUndefined()
    expect(doc.isUploading).toBe(false)
    unmount()
  })

  it('flags generic when backend returns an unrelated error', async () => {
    uploadMutateMock.mockRejectedValue(new Error('network down'))
    const { api, unmount } = setup()

    await api.addFiles([makeFile('report.pdf')])

    expect(api.documents.value[0].error).toBe('generic')
    unmount()
  })

  it('flags generic for a 400 without a refusal or password code', async () => {
    uploadMutateMock.mockRejectedValue(
      makeAxiosBadRequest({ 'File.Upload.Failed': ['Some other failure.'] }),
    )
    const { api, unmount } = setup()

    await api.addFiles([makeFile('report.pdf')])

    expect(api.documents.value[0].error).toBe('generic')
    unmount()
  })

  it("shows the platform's storage-limit refusal on the file's row, and lets it be retried", async () => {
    const storageFull = 'Your account has reached its storage limit. Delete documents you no longer need, or contact support.'
    uploadMutateMock.mockRejectedValue(axiosError(403, { title: storageFull, status: 403, code: 'File.Refused.StorageFull' }))
    const { api, unmount } = setup()

    await api.addFiles([makeFile('report.pdf')])

    const doc = api.documents.value[0]
    expect(doc.error).toBe('limited')
    expect(doc.errorDetail).toBe(storageFull)
    expect(api.retryableFailures.value).toHaveLength(1)
    unmount()
  })

  it("shows the hourly upload refusal on the file's row with the time in the reader's clock", async () => {
    uploadMutateMock.mockRejectedValue(axiosError(429, {
      title: 'Your organization has uploaded the most files allowed in an hour. You can upload more from 14:31 UTC.',
      status: 429,
      code: 'File.Refused.TooManyUploads',
      resetsAt: '2026-11-03T14:31:00Z',
    }))
    const { api, unmount } = setup()

    await api.addFiles([makeFile('report.pdf')])

    const doc = api.documents.value[0]
    expect(doc.error).toBe('limited')
    expect(doc.errorDetail).toMatch(/^Your organization has uploaded the most files allowed in an hour\. You can upload more from .+ (today|tomorrow)\.$/)
    expect(doc.errorDetail).not.toContain('UTC')
    unmount()
  })

  it('flags passwordRequired when backend returns File.Password.Required', async () => {
    uploadMutateMock.mockRejectedValue(
      makeAxiosBadRequest({ 'File.Password.Required': ['The file is password-protected.'] }),
    )
    const { api, unmount } = setup()

    await api.addFiles([makeFile('locked.pdf')])

    const doc = api.documents.value[0]
    expect(doc.error).toBe('passwordRequired')
    expect(doc.id).toBeUndefined()
    unmount()
  })

  it('flags passwordIncorrect when backend returns File.Password.Incorrect', async () => {
    uploadMutateMock.mockRejectedValue(
      makeAxiosBadRequest({ 'File.Password.Incorrect': ['The password is incorrect.'] }),
    )
    const { api, unmount } = setup()

    await api.addFiles([makeFile('locked.pdf')])

    expect(api.documents.value[0].error).toBe('passwordIncorrect')
    unmount()
  })

  it('flags passwordUnsupported when backend returns File.Password.Unsupported', async () => {
    uploadMutateMock.mockRejectedValue(
      makeAxiosBadRequest({ 'File.Password.Unsupported': ['Certificate protection cannot be removed.'] }),
    )
    const { api, unmount } = setup()

    await api.addFiles([makeFile('drm.pdf')])

    expect(api.documents.value[0].error).toBe('passwordUnsupported')
    unmount()
  })

  it('matches error codes case-insensitively (ASP.NET camelCases the first key segment on the wire)', async () => {
    uploadMutateMock.mockRejectedValue(
      makeAxiosBadRequest({ 'file.Password.Required': ['The file is password-protected.'] }),
    )
    const { api, unmount } = setup()

    await api.addFiles([makeFile('locked.pdf')])

    expect(api.documents.value[0].error).toBe('passwordRequired')
    unmount()
  })


  it('never passes the raw error (which carries the password FormData) to console output', async () => {
    const SECRET_PASSWORD = 'secret-pw-do-not-log'
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    try {
      uploadMutateMock.mockRejectedValue(
        makeAxiosBadRequest({ 'File.Password.Incorrect': ['The password is incorrect.'] }),
      )
      const { api, unmount } = setup()

      await api.addFiles([makeFile('locked.pdf')])
      await api.uploadWithPassword(api.documents.value[0].ref, SECRET_PASSWORD)

      expect(consoleErrorSpy).toHaveBeenCalled()
      const loggedValues = consoleErrorSpy.mock.calls.flat()
      expect(loggedValues.every(value => typeof value === 'string')).toBe(true)
      expect(loggedValues.join(' ')).not.toContain(SECRET_PASSWORD)
      unmount()
    } finally {
      consoleErrorSpy.mockRestore()
    }
  })

  it('uploadWithPassword re-sends the file with a password and clears the error on success', async () => {
    uploadMutateMock.mockRejectedValueOnce(
      makeAxiosBadRequest({ 'File.Password.Required': ['The file is password-protected.'] }),
    )
    const { api, unmount } = setup()

    await api.addFiles([makeFile('locked.pdf')])
    expect(api.documents.value[0].error).toBe('passwordRequired')

    uploadMutateMock.mockResolvedValueOnce({ id: 'doc-unlocked', filename: 'locked.pdf' })
    await api.uploadWithPassword(api.documents.value[0].ref, 'test-123')

    const doc = api.documents.value[0]
    expect(doc.id).toBe('doc-unlocked')
    expect(doc.error).toBeUndefined()

    const lastCall = uploadMutateMock.mock.calls.at(-1)?.[0]
    const sentFormData = lastCall.formData as FormData
    expect(sentFormData.get('password')).toBe('test-123')
    unmount()
  })

  it('uploadWithPassword keeps the row in passwordIncorrect when the password is wrong', async () => {
    uploadMutateMock.mockRejectedValueOnce(
      makeAxiosBadRequest({ 'File.Password.Required': ['The file is password-protected.'] }),
    )
    const { api, unmount } = setup()

    await api.addFiles([makeFile('locked.pdf')])

    uploadMutateMock.mockRejectedValueOnce(
      makeAxiosBadRequest({ 'File.Password.Incorrect': ['The password is incorrect.'] }),
    )
    await api.uploadWithPassword(api.documents.value[0].ref, 'wrong')

    expect(api.documents.value[0].error).toBe('passwordIncorrect')
    expect(api.documents.value[0].id).toBeUndefined()
    unmount()
  })

  it('excludes errored documents from the form payload', async () => {
    uploadMutateMock.mockRejectedValue(new Error('network down'))
    const { api, unmount } = setup()

    await api.addFiles([makeFile('report.pdf')])

    expect(api.documents.value).toHaveLength(1)
    expect(api.getFormDocuments()).toEqual([])
    unmount()
  })

  it('keeps successful documents in the form payload', async () => {
    uploadMutateMock.mockResolvedValue({ id: 'doc-1', filename: 'report.pdf' })
    const { api, unmount } = setup()

    await api.addFiles([makeFile('report.pdf')])

    const formDocs = api.getFormDocuments()
    expect(formDocs).toHaveLength(1)
    expect(formDocs[0].id).toBe('doc-1')
    unmount()
  })

  it('carries warnings through the form payload and back via setDocuments', async () => {
    uploadMutateMock.mockResolvedValue({
      id: 'doc-1',
      filename: 'email.msg.pdf',
      warnings: ['Email had 2 attachment(s).'],
    })
    const { api, unmount } = setup()

    await api.addFiles([makeFile('email.msg')])
    const formDocs = api.getFormDocuments()
    expect(formDocs[0].warnings).toEqual(['Email had 2 attachment(s).'])

    api.setDocuments(formDocs)
    expect(api.documents.value[0].warnings).toEqual(['Email had 2 attachment(s).'])
    unmount()
  })
})
