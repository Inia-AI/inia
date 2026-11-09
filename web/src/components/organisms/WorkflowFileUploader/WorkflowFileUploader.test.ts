import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { badRequest, selectFiles, uploadMutateMock } from '@/test/helpers/uploaderMocks'
import { axiosError } from '@/test/helpers/axiosError'

vi.mock('@/api', async () => (await import('@/test/helpers/uploaderMocks')).apiModule)
vi.mock('vue-i18n', async () => (await import('@/test/helpers/uploaderMocks')).i18nModule)
vi.mock('@/components/ui/dialog', async () => (await import('@/test/helpers/uploaderMocks')).dialogModule)
vi.mock('vee-validate', async () => (await import('@/test/helpers/uploaderMocks')).veeValidateModule)
vi.mock('@/components/ui/form', async () => (await import('@/test/helpers/uploaderMocks')).formModule)
vi.mock('@/components/atoms', async () => (await import('@/test/helpers/uploaderMocks')).atomsModule)
vi.mock('@/components/molecules', async () => (await import('@/test/helpers/uploaderMocks')).moleculesModule)

import WorkflowFileUploader from './WorkflowFileUploader.vue'

const passwordRequiredError = () =>
  badRequest({ 'file.Password.Required': ['The file is password-protected.'] })

function mountUploader(): VueWrapper {
  return mount(WorkflowFileUploader, {
    props: { name: 'files', modelValue: [] },
  })
}

function dialogOpen(wrapper: VueWrapper): boolean {
  return wrapper.find('[data-stub="dialog"]').attributes('data-open') === 'true'
}

function dialogTitle(wrapper: VueWrapper): string {
  return wrapper.find('[data-stub="dialog-title"]').text()
}

function findButtonByText(wrapper: VueWrapper, needle: string) {
  const button = wrapper
    .findAll('button')
    .find(candidate => candidate.text().includes(needle))
  expect(button, `no button containing "${needle}"`).toBeDefined()
  return button!
}

describe('WorkflowFileUploader upload outcomes', () => {
  beforeEach(() => {
    uploadMutateMock.mockReset()
  })

  it('shows an upload warning under the file name without hovering', async () => {
    const warning = 'This PDF file appears to be incomplete or damaged. Processing will attempt to repair it automatically.'
    uploadMutateMock.mockResolvedValueOnce({
      id: 'doc-1',
      filename: 'damaged.pdf',
      mediaType: 'application/pdf',
      warnings: [warning],
    })
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['damaged.pdf'])

    const warnings = wrapper.findAll('.file-warning')
    expect(warnings).toHaveLength(1)
    expect(warnings[0].text()).toBe(warning)
    expect(wrapper.find('.file-card').classes()).not.toContain('has-error')
  })

  it('shows the pages the platform counted beside the file name, and nothing when it could not count them', async () => {
    uploadMutateMock
      .mockResolvedValueOnce({ id: 'doc-1', filename: 'ledger', mediaType: 'TextCsv', countedPages: 12 })
      .mockResolvedValueOnce({ id: 'doc-2', filename: 'broken', mediaType: 'ApplicationPdf', countedPages: null })
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['ledger.csv', 'broken.pdf'])

    const pages = wrapper.findAll('.file-pages')
    expect(pages).toHaveLength(1)
    expect(pages[0].text()).toBe('shared.components.fileControl.pages:{"n":12}')
  })

  it('shows the intake gate\'s reason when the server refuses the file', async () => {
    const reason = 'The content is application/vnd.microsoft.portable-executable; the platform reads no such document.'
    uploadMutateMock.mockRejectedValueOnce(
      badRequest({ 'file.Refused.FileTypeMismatch': [reason] }),
    )
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['renamed.pdf'])

    expect(wrapper.find('.file-card').classes()).toContain('has-error')
    expect(wrapper.find('.file-error').text()).toBe(reason)
  })

  it("shows the storage-limit refusal on the file's row and offers a retry once space is freed", async () => {
    const storageFull = 'Your account has reached its storage limit. Delete documents you no longer need, or contact support.'
    uploadMutateMock.mockRejectedValueOnce(axiosError(403, { title: storageFull, status: 403, code: 'File.Refused.StorageFull' }))
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['statement.pdf'])

    expect(wrapper.find('.file-card').classes()).toContain('has-error')
    expect(wrapper.find('.file-error').text()).toBe(storageFull)
    expect(wrapper.find('.upload-error-summary').exists()).toBe(true)
  })

  it("shows the hourly upload refusal on the file's row with the time it can go again", async () => {
    uploadMutateMock.mockRejectedValueOnce(axiosError(429, {
      title: 'Your organization has uploaded the most files allowed in an hour. You can upload more from 14:31 UTC.',
      status: 429,
      code: 'File.Refused.TooManyUploads',
      resetsAt: '2026-11-03T14:31:00Z',
    }))
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['statement.pdf'])

    const [key, params] = wrapper.find('.file-error').text().split(/:(.*)/s)
    expect(key).toBe('shared.components.fileControl.tooManyUploads')
    expect(JSON.parse(params)).toEqual({ time: expect.stringMatching(/\d/), day: expect.stringMatching(/^shared\.dailyAllowance\.(today|tomorrow)$/) })
    expect(wrapper.find('.upload-error-summary').exists()).toBe(true)
  })

  it('keeps a refused file out of the retry banner, because retrying cannot change the verdict', async () => {
    const reason = 'The file is named as a PDF, but its content is plain text.'
    uploadMutateMock.mockRejectedValueOnce(
      badRequest({ 'file.Refused.FileTypeMismatch': [reason] }),
    )
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['not-a-real-document.pdf'])

    expect(wrapper.find('.file-error').text()).toBe(reason)
    expect(wrapper.find('.upload-error-summary').exists()).toBe(false)
  })

  it('keeps a file whose protection the platform cannot open out of the retry banner', async () => {
    uploadMutateMock.mockRejectedValueOnce(badRequest({ 'file.Password.Unsupported': ['Unsupported.'] }))
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['sealed.pdf'])

    expect(wrapper.find('.file-error').exists()).toBe(true)
    expect(wrapper.find('.upload-error-summary').exists()).toBe(false)
  })

  it('offers retry for a failed upload beside a refused one, and retries only the failed one', async () => {
    uploadMutateMock
      .mockRejectedValueOnce(badRequest({ 'file.Refused.FileTypeMismatch': ['Refused.'] }))
      .mockRejectedValueOnce(new Error('network down'))
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['renamed.pdf', 'statement.pdf'])

    expect(wrapper.find('.upload-error-summary').text()).toContain('{"count":1}')
    uploadMutateMock.mockResolvedValueOnce({ id: 'doc-2', filename: 'statement.pdf', mediaType: 'application/pdf' })

    await findButtonByText(wrapper, 'shared.components.fileControl.retry').trigger('click')
    await flushPromises()

    expect(uploadMutateMock).toHaveBeenCalledTimes(3)
    expect(wrapper.find('.upload-error-summary').exists()).toBe(false)
    expect(wrapper.find('.file-error').text()).toBe('Refused.')
  })

  it('dismisses only the failures the banner counts, and keeps a refused file with its reason', async () => {
    uploadMutateMock
      .mockRejectedValueOnce(badRequest({ 'file.Refused.FileTypeMismatch': ['Refused.'] }))
      .mockRejectedValueOnce(new Error('network down'))
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['renamed.pdf', 'statement.pdf'])
    await findButtonByText(wrapper, 'shared.components.fileControl.dismiss').trigger('click')
    await flushPromises()

    expect(wrapper.find('.upload-error-summary').exists()).toBe(false)
    expect(wrapper.findAll('.file-error').map(error => error.text())).toEqual(['Refused.'])
  })

  it('falls back to the generic message when a failure carries no refusal key', async () => {
    uploadMutateMock.mockRejectedValueOnce(badRequest({ 'file': ['Something else.'] }))
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['other.pdf'])

    expect(wrapper.find('.file-error').text()).toBe('shared.components.fileControl.generic')
  })
})

describe('WorkflowFileUploader password prompt', () => {
  beforeEach(() => {
    uploadMutateMock.mockReset()
  })

  it('auto-opens the unlock dialog when an upload comes back passwordRequired', async () => {
    uploadMutateMock.mockRejectedValue(passwordRequiredError())
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['locked.pdf'])

    expect(dialogOpen(wrapper)).toBe(true)
    expect(dialogTitle(wrapper)).toContain('locked.pdf')
    wrapper.unmount()
  })

  it('re-opens the prompt for a previously skipped file when Retry is clicked', async () => {
    uploadMutateMock.mockRejectedValue(passwordRequiredError())
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['locked.pdf'])
    expect(dialogOpen(wrapper)).toBe(true)

    await findButtonByText(wrapper, 'passwordPrompt.skip').trigger('click')
    expect(dialogOpen(wrapper)).toBe(false)

    await findButtonByText(wrapper, 'fileControl.retry').trigger('click')
    await flushPromises()

    expect(dialogOpen(wrapper)).toBe(true)
    expect(dialogTitle(wrapper)).toContain('locked.pdf')
    wrapper.unmount()
  })

  it('moves the prompt to the next flagged file when the pinned file is removed', async () => {
    uploadMutateMock.mockRejectedValue(passwordRequiredError())
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['a.pdf', 'b.pdf'])
    expect(dialogOpen(wrapper)).toBe(true)
    expect(dialogTitle(wrapper)).toContain('a.pdf')

    await wrapper.find(`[aria-label='shared.components.fileControl.removeFile:{"name":"a.pdf"}']`).trigger('click')
    await flushPromises()

    expect(dialogOpen(wrapper)).toBe(true)
    expect(dialogTitle(wrapper)).toContain('b.pdf')
    wrapper.unmount()
  })
})

describe('WorkflowFileUploader without the email and Excel 97-2003 module', () => {
  beforeEach(() => {
    uploadMutateMock.mockReset()
    uploadMutateMock.mockResolvedValue({ id: 'doc-1' })
  })

  it('sends a dropped Excel 97-2003 or email file to the platform, whose refusal says what to do instead', async () => {
    const wrapper = mountUploader()

    await wrapper.find('.dropzone').trigger('drop', {
      dataTransfer: {
        files: [new File(['x'], 'report.xls'), new File(['x'], 'mail.msg'), new File(['x'], 'mail.eml')],
      },
    })
    await flushPromises()

    expect(uploadMutateMock).toHaveBeenCalledTimes(3)
    expect(wrapper.find('.rejected-drop-note').exists()).toBe(false)
    wrapper.unmount()
  })

  it('shows the platform refusal of an email file in its row', async () => {
    const refusal =
      'The platform does not read email files (.eml). Save the email as a PDF, or upload its attachments instead. The full edition reads email files directly and extracts their attachments.'
    uploadMutateMock.mockRejectedValueOnce(badRequest({ 'File.Refused.FormatNotRead': [refusal] }))
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['mail.eml'])

    expect(wrapper.find('.file-error').text()).toBe(refusal)
    wrapper.unmount()
  })

  it('sends a picked email file to the platform, because the picker can be set to show every file type', async () => {
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['statement.pdf', 'mail.eml'])

    expect(uploadMutateMock).toHaveBeenCalledTimes(2)
    expect(wrapper.find('.rejected-drop-note').exists()).toBe(false)
    wrapper.unmount()
  })

  it('names neither Excel 97-2003 nor email in the picker filter or the hint, and offers no attachment option', async () => {
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['statement.pdf'])

    expect(wrapper.find('#workflow-file-uploader-input').attributes('accept')).toBe(
      '.pdf, .xlsx, .xlsm, .docx, .csv, .jpg, .jpeg, .png',
    )
    expect(wrapper.find('.dropzone-hint').text()).not.toMatch(/\bXLS\b|MSG|EML/)
    expect(wrapper.find('.email-intake-panel').exists()).toBe(false)
    wrapper.unmount()
  })

  it('accepts a dropped Word file', async () => {
    const wrapper = mountUploader()

    await wrapper.find('.dropzone').trigger('drop', { dataTransfer: { files: [new File(['x'], 'baseline report.docx')] } })
    await flushPromises()

    expect(uploadMutateMock).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('baseline report.docx')
    expect(wrapper.find('.rejected-drop-note').exists()).toBe(false)
    wrapper.unmount()
  })

  it('accepts a dropped PDF and rejects unsupported extensions', async () => {
    const wrapper = mountUploader()

    await wrapper.find('.dropzone').trigger('drop', {
      dataTransfer: { files: [new File(['x'], 'statement.pdf'), new File(['x'], 'slides.pptx')] },
    })
    await flushPromises()

    expect(uploadMutateMock).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.rejected-drop-note').text()).toContain('slides.pptx')
    wrapper.unmount()
  })
})

describe('WorkflowFileUploader class select', () => {
  beforeEach(() => {
    uploadMutateMock.mockReset()
  })

  it('shows the empty class option for a file with no class, not a blank select', async () => {
    uploadMutateMock.mockResolvedValueOnce({ id: 'doc-1', filename: 'statement.pdf', mediaType: 'application/pdf' })
    const wrapper = mountUploader()

    await selectFiles(wrapper, ['statement.pdf'])

    const select = wrapper.findComponent({ name: 'inia-selectStub' })
    expect(select.vm.$attrs['model-value']).toBe('')
    wrapper.unmount()
  })
})

describe('WorkflowFileUploader keyboard access', () => {
  it.each(['Enter', ' '])('takes focus and opens the file picker on %j', async key => {
    const wrapper = mountUploader()
    const dropzone = wrapper.find('.dropzone')
    const input = wrapper.find('#workflow-file-uploader-input').element as HTMLInputElement
    const openPicker = vi.spyOn(input, 'click').mockImplementation(() => {})

    expect(dropzone.attributes('tabindex')).toBe('0')
    expect(dropzone.attributes('role')).toBe('button')

    await dropzone.trigger('keydown', { key })

    expect(openPicker).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })
})
