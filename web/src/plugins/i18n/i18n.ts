import { createI18n } from "vue-i18n";

import { LANGUAGES, DEFAULT_LOCALE } from "@/assets/locales";

export default createI18n({
  legacy: false,
  globalInjection: true,
  locale: DEFAULT_LOCALE,
  fallbackLocale: DEFAULT_LOCALE,
  messages: LANGUAGES,
  warnHtmlMessage: false,
});
