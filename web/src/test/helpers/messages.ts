import { createI18n } from 'vue-i18n'
import { EN as OPEN_EN } from '@/assets/locales/en'

const moduleMessages = import.meta.glob<{ messages: Record<string, unknown> }>(
  ['/src/modules/*/locales/en/index.ts', '/src/modules/*/locales/en/page/index.ts'],
  { eager: true },
)

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: structuredClone(OPEN_EN) } })
for (const module of Object.values(moduleMessages)) {
  i18n.global.mergeLocaleMessage('en', module.messages)
}

/** The English messages of the open build and of every module this tree still holds, merged as the app merges them. */
export const EN = i18n.global.getLocaleMessage('en') as typeof OPEN_EN & Record<string, unknown>
