import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

// `@/components/ui/dialog` portals via reka-ui in real use, which would yank
// the content out of the wrapper's tree in jsdom. Stub each piece with a
// transparent passthrough so we can interact with the cards directly. The
// behavior we care about (click → emit, X/Esc/overlay → emit) lives in our
// component, not in the dialog primitives.
vi.mock('@/components/ui/dialog', () => {
  const passthrough = (label: string) =>
    defineComponent({
      name: `${label}Stub`,
      setup(_, { slots }) {
        return () => h('div', { 'data-stub': label }, slots.default?.())
      },
    })
  return {
    Dialog: defineComponent({
      name: 'DialogStub',
      props: ['open'],
      emits: ['update:open'],
      setup(props, { slots }) {
        return () =>
          h(
            'div',
            { 'data-stub': 'Dialog', 'data-open': String(props.open) },
            slots.default?.(),
          )
      },
    }),
    DialogContent: passthrough('DialogContent'),
    DialogHeader: passthrough('DialogHeader'),
    DialogTitle: passthrough('DialogTitle'),
    DialogDescription: passthrough('DialogDescription'),
  }
})

vi.mock('@lucide/vue', () => ({
  RotateCw: defineComponent({ name: 'RotateCwStub', template: '<span />' }),
  Pencil: defineComponent({ name: 'PencilStub', template: '<span />' }),
}))

import ReRunChoiceDialog from './ReRunChoiceDialog.vue'

const mountDialog = () =>
  mount(ReRunChoiceDialog, {
    props: { modelValue: true },
  })

const findChoice = (wrapper: ReturnType<typeof mountDialog>, label: string) =>
  wrapper
    .findAll('[role="button"]')
    .find(el => el.text().includes(label))!

describe('ReRunChoiceDialog', () => {
  it('renders the title, description, and a hint under each choice', () => {
    const wrapper = mountDialog()
    const html = wrapper.html()

    expect(html).toContain('shared.reRunChoice.title')
    expect(html).toContain('shared.reRunChoice.description')
    expect(html).toContain('shared.reRunChoice.restart')
    expect(html).toContain('shared.reRunChoice.restartHint')
    expect(html).toContain('shared.reRunChoice.editInputs')
    expect(html).toContain('shared.reRunChoice.editInputsHint')
  })

  it('emits `restart` and closes when the user picks Restart', async () => {
    const wrapper = mountDialog()
    await findChoice(wrapper, 'shared.reRunChoice.restart').trigger('click')

    expect(wrapper.emitted('restart')).toHaveLength(1)
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
    expect(wrapper.emitted('edit')).toBeUndefined()
  })

  it('emits `edit` and closes when the user picks Edit inputs first', async () => {
    const wrapper = mountDialog()
    await findChoice(wrapper, 'shared.reRunChoice.editInputs').trigger('click')

    expect(wrapper.emitted('edit')).toHaveLength(1)
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
    expect(wrapper.emitted('restart')).toBeUndefined()
  })

  it('forwards Dialog open changes through update:modelValue (X / Esc / overlay)', async () => {
    const wrapper = mountDialog()
    // Dismissal via the built-in close affordances surfaces as `update:open`
    // from the Dialog stub. Our component should translate that into a
    // v-model change without claiming either choice.
    await wrapper
      .findComponent({ name: 'DialogStub' })
      .vm.$emit('update:open', false)

    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
    expect(wrapper.emitted('restart')).toBeUndefined()
    expect(wrapper.emitted('edit')).toBeUndefined()
  })
})
