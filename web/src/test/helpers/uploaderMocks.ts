import { defineComponent, h } from 'vue'
import { flushPromises, type VueWrapper } from '@vue/test-utils'
import { vi } from 'vitest'
import { axiosError } from '@/test/helpers/axiosError'

/** The upload call that every mocked `@/api` below hands to the uploader. */
export const uploadMutateMock = vi.fn()

const slotStub = (label: string) => ({
  name: `${label}Stub`,
  props: {},
  setup(_: unknown, { slots }: { slots: Record<string, undefined | ((scope: object) => unknown)> }) {
    return () => h('div', { 'data-stub': label }, slots.default?.({ componentField: {}, field: {} }) as never)
  },
})

export const apiModule = {
  default: {
    Files: {
      useUploadDocument: () => ({ mutateAsync: uploadMutateMock }),
      service: { uploadEndpoint: () => 'https://api.example.com/api/v1/files' },
    },
  },
}

export const i18nModule = {
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}

// reka-ui's Dialog portals out of the wrapper tree under jsdom, so stub the
// dialog primitives; the root stub renders its slot only while open, mirroring
// the real DialogContent mount behavior.
export const dialogModule = {
  Dialog: defineComponent({
    name: 'DialogStub',
    props: { open: { type: Boolean, default: false } },
    emits: ['update:open'],
    setup(props, { slots }) {
      return () => h('div', { 'data-stub': 'dialog', 'data-open': String(props.open) }, props.open ? slots.default?.() : [])
    },
  }),
  DialogContent: slotStub('dialog-content'),
  DialogHeader: slotStub('dialog-header'),
  DialogTitle: slotStub('dialog-title'),
  DialogDescription: slotStub('dialog-description'),
  DialogFooter: slotStub('dialog-footer'),
}

export const veeValidateModule = { Field: slotStub('form-field') }

export const formModule = {
  FormItem: slotStub('form-item'),
  FormLabel: slotStub('form-label'),
  FormControl: slotStub('form-control'),
  FormMessage: slotStub('form-message'),
}

export const atomsModule = {
  IniaIcon: defineComponent({
    name: 'IniaIconStub',
    setup() {
      return () => h('span')
    },
  }),
}

export const moleculesModule = { IniaSelect: slotStub('inia-select') }

/** Picks files through the uploader's file input, and waits for any panel a module loads lazily. */
export async function selectFiles(wrapper: VueWrapper, names: string[]) {
  const input = wrapper.find('#workflow-file-uploader-input')
  Object.defineProperty(input.element, 'files', {
    value: names.map(name => new File(['x'], name, { type: 'application/pdf' })),
    configurable: true,
  })
  await input.trigger('change')
  await flushPromises()
  await vi.dynamicImportSettled()
  await flushPromises()
}

/** The upload endpoint's 400 answer with these error keys. */
export const badRequest = (errors: Record<string, string[]>) => axiosError(400, { errors })
