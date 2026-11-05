import { defineComponent, h, type Component } from 'vue'
import { useCapabilities } from '@/composables/useCapabilities'

export const withCapability = (capability: string, piece: Component) =>
  defineComponent({
    inheritAttrs: false,
    setup(_, { attrs }) {
      const has = useCapabilities()
      return () => (has(capability) ? h(piece, attrs) : null)
    },
  })
