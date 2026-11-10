import { onUnmounted, ref, toValue, watchEffect, type MaybeRefOrGetter } from 'vue'
import { useTitle } from '@vueuse/core'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

const APP_NAME = 'Inia AI'

const itemName = ref<string>()
let itemNameOwner: object | undefined

export function useDocumentTitle() {
  const route = useRoute()
  const { t } = useI18n()

  useTitle(() =>
    [itemName.value, route.meta.title ? t(route.meta.title) : undefined, APP_NAME].filter(Boolean).join(' · '),
  )
}

/** Vue unmounts the page being left after the next page has set its name, so only the last writer clears it. */
export function useItemTitle(name: MaybeRefOrGetter<string | undefined>) {
  const owner = {}
  watchEffect(() => {
    itemNameOwner = owner
    itemName.value = toValue(name)
  })
  onUnmounted(() => {
    if (itemNameOwner === owner) {
      itemNameOwner = undefined
      itemName.value = undefined
    }
  })
}
