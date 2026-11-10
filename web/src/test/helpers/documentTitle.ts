import { defineComponent, h, type Component } from 'vue'
import { useDocumentTitle } from '@/composables/useDocumentTitle'

export const withDocumentTitle = (page: Component) =>
  defineComponent({
    setup() {
      useDocumentTitle()
      return () => h(page)
    },
  })
