import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import type { RouteRecordRaw } from 'vue-router'
import router from './index'
import i18n from '@/plugins/i18n/i18n'
import { PAID_MODULES } from '@/modules'
import { EN } from '@/test/helpers/messages'

type Route = Pick<RouteRecordRaw, 'path' | 'redirect' | 'component' | 'components' | 'meta'>

const untitledPaths = (routes: Route[], hasMessage: (key: string) => boolean) =>
  routes
    .filter(route => !route.redirect && Boolean(route.component ?? route.components))
    .filter(route => !route.meta?.title || !hasMessage(route.meta.title))
    .map(route => route.path)

describe('route titles', () => {
  it('gives every page of the app a name for the browser tab', () => {
    expect(untitledPaths(router.getRoutes(), key => i18n.global.te(key, 'en'))).toEqual([])
  })

  it('gives every page of a paid module a name for the browser tab', () => {
    const withModules = createI18n({ legacy: false, locale: 'en', messages: { en: EN } })
    const moduleRoutes = PAID_MODULES.flatMap(module => module.routes ?? [])

    expect(untitledPaths(moduleRoutes, key => withModules.global.te(key, 'en'))).toEqual([])
  })
})
