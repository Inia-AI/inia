import type { InjectionKey, Ref } from 'vue'

export const FORM_ITEM_INJECTION_KEY
  = Symbol() as InjectionKey<string>

export const FORM_DESCRIPTION_SHOWN_INJECTION_KEY
  = Symbol() as InjectionKey<Ref<boolean>>
