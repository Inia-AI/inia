import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'
import { queryClient } from '@/plugins/queryClient'

import registerPlugins from './register'
import { registerModules } from './modules'
import { i18n, router } from './plugins'

import App from './App.vue'
import { isOidcPopupCallback } from '@/lib/oidcPopup'
import { reloadOnStaleChunk } from '@/lib/staleChunkReload'

reloadOnStaleChunk()

if (isOidcPopupCallback()) {
  window.name = ''
  import('@/lib/oidc')
    .then(({ completeOidcPopupCallback }) => completeOidcPopupCallback())
    .catch(() => window.close())
} else {
  const app = createApp(App)

  registerModules(router)
  app.use(createPinia())
  app.use(router)
  app.use(i18n)
  app.use(VueQueryPlugin, { queryClient })

  registerPlugins(app)

  app.mount('#app')
}
