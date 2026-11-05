import type { Router } from 'vue-router'
import i18n from '@/plugins/i18n/i18n'
import { extensionPoints, type AfterScreens, type PaidModule } from './extensionPoints'

export const PAID_MODULES: PaidModule[] = [
]

function screensLoaderOf({ loadScreens }: PaidModule): (() => Promise<void>) | undefined {
  if (!loadScreens) {
    return undefined
  }
  let screens: Promise<void> | undefined
  return () =>
    (screens ??= loadScreens().then(
      loaded => i18n.global.mergeLocaleMessage('en', loaded.messages),
      error => {
        screens = undefined
        throw error
      },
    ))
}

export function registerModules(router: Router, modules = PAID_MODULES) {
  for (const module of modules) {
    i18n.global.mergeLocaleMessage('en', module.messages)
    const loadScreens = screensLoaderOf(module)
    const afterScreens: AfterScreens = loadScreens
      ? load => () => Promise.all([loadScreens(), load()]).then(([, loaded]) => loaded)
      : load => load
    for (const route of module.routes ?? []) {
      const gated = module.capability ? { ...route, meta: { ...route.meta, capability: module.capability } } : route
      router.addRoute(loadScreens ? { ...gated, beforeEnter: [loadScreens, ...[gated.beforeEnter ?? []].flat()] } : gated)
    }
    module.extend(extensionPoints, afterScreens)
  }
}
