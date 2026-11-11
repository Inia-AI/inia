import { defineComponent, h } from 'vue'

export const passthrough = (name: string) =>
  defineComponent({
    name,
    setup(_, { slots }) {
      return () => h('div', { 'data-stub': name }, slots.default?.())
    },
  })

export const popoverModule = {
  Popover: passthrough('Popover'),
  PopoverTrigger: passthrough('PopoverTrigger'),
  PopoverContent: passthrough('PopoverContent'),
}

export const inputStub = (name: string) =>
  defineComponent({
    name: `${name}Stub`,
    props: ['modelValue', 'placeholder'],
    emits: ['update:modelValue'],
    setup(props, { emit }) {
      return () =>
        h('input', {
          'data-stub': name,
          value: props.modelValue ?? '',
          placeholder: props.placeholder ?? '',
          onInput: (e: Event) =>
            emit('update:modelValue', (e.target as HTMLInputElement).value),
        })
    },
  })

export const buttonStub = defineComponent({
  name: 'ButtonStub',
  props: { type: { type: String, default: 'button' }, disabled: Boolean },
  setup(props, { slots }) {
    return () => h('button', { type: props.type, disabled: props.disabled }, slots.default?.())
  },
})

export const dialogStubs = {
  Dialog: passthrough('Dialog'),
  DialogContent: passthrough('DialogContent'),
  DialogHeader: passthrough('DialogHeader'),
  DialogTitle: passthrough('DialogTitle'),
  DialogDescription: passthrough('DialogDescription'),
  DialogFooter: passthrough('DialogFooter'),
}

export const commandModule = {
  Command: passthrough('Command'),
  CommandList: passthrough('CommandList'),
  CommandGroup: passthrough('CommandGroup'),
  CommandEmpty: passthrough('CommandEmpty'),
  CommandSeparator: passthrough('CommandSeparator'),
  CommandInput: inputStub('CommandInput'),
  CommandItem: defineComponent({
    name: 'CommandItemStub',
    props: { value: null, disabled: Boolean },
    emits: ['select'],
    setup(props, { slots, emit }) {
      return () =>
        h(
          'div',
          {
            'data-stub': 'CommandItem',
            role: 'option',
            'data-disabled': props.disabled ? 'true' : undefined,
            'aria-disabled': props.disabled ? 'true' : undefined,
            onClick: () => {
              if (!props.disabled) emit('select')
            },
          },
          slots.default?.(),
        )
    },
  }),
}

export const scrollAreaModule = {
  ScrollArea: passthrough('ScrollArea'),
}

export const lucideModule = (names: readonly string[]): Record<string, unknown> => {
  const out: Record<string, unknown> = {}
  for (const n of names) {
    out[n] = defineComponent({ name: `${n}Stub`, template: '<span />' })
  }
  return out
}

export const checkboxStub = defineComponent({
  name: 'CheckboxStub',
  props: { modelValue: Boolean, id: String },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () =>
      h('input', {
        type: 'checkbox',
        id: props.id,
        checked: props.modelValue,
        onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).checked),
      })
  },
})
