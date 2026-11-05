import { describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import i18n from '@/plugins/i18n/i18n'
import { extensionPoints, type AfterScreens, type PaidModule } from './extensionPoints'
import { PAID_MODULES, registerModules } from './index'

describe('registerModules', () => {
  it('adds every module routes and labels, and hands each module the extension points', () => {
    const router = createRouter({ history: createMemoryHistory(), routes: [] })
    const extend = PAID_MODULES.map(module => vi.spyOn(module, 'extend'))

    registerModules(router)

    for (const module of PAID_MODULES) {
      for (const route of module.routes ?? []) {
        expect(router.getRoutes().some(added => added.path === route.path), route.path).toBe(true)
      }
      expect(i18n.global.getLocaleMessage('en')).toMatchObject(module.messages)
    }
    for (const spy of extend) {
      expect(spy).toHaveBeenCalledWith(extensionPoints, expect.any(Function))
    }
  })
})

const SCREEN_TEXT = { messages: { page: { sampleScreen: { title: 'Sample screen' } } } }

const routerWithHome = () =>
  createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { render: () => null } }] })

const sampleModule = (loadScreens: PaidModule['loadScreens'], extend: PaidModule['extend'] = () => {}): PaidModule => ({
  messages: {},
  loadScreens,
  routes: [{ path: '/sample', component: () => Promise.resolve({ render: () => null }) }],
  extend,
})

describe('a module whose screens load later', () => {
  it('loads its screens once, for its first route, and not again for a later route or piece', async () => {
    const loadScreens = vi.fn(async () => SCREEN_TEXT)
    let afterScreens: AfterScreens = load => load
    const router = routerWithHome()
    registerModules(router, [sampleModule(loadScreens, (_, gate) => (afterScreens = gate))])
    expect(loadScreens).not.toHaveBeenCalled()

    await router.push('/sample')
    expect(loadScreens).toHaveBeenCalledTimes(1)
    await router.push('/')
    await router.push('/sample')
    await afterScreens(async () => 'piece')()

    expect(loadScreens).toHaveBeenCalledTimes(1)
    expect(i18n.global.t('page.sampleScreen.title')).toBe('Sample screen')
  })

  it('loads its screens before a child route, an eager component, and the route\'s own guard', async () => {
    const loadScreens = vi.fn(async () => ({ messages: { page: { parentScreen: { title: 'Parent screen' } } } }))
    const textAtOwnGuard: boolean[] = []
    const screen = { render: () => null }
    const router = routerWithHome()
    registerModules(router, [
      {
        messages: {},
        loadScreens,
        routes: [
          {
            path: '/parent',
            component: screen,
            beforeEnter: () => {
              textAtOwnGuard.push(i18n.global.te('page.parentScreen.title'))
            },
            children: [{ path: 'child', component: screen }],
          },
        ],
        extend: () => {},
      },
    ])

    await router.push('/parent/child')

    expect(router.currentRoute.value.fullPath).toBe('/parent/child')
    expect(loadScreens).toHaveBeenCalledTimes(1)
    expect(textAtOwnGuard).toEqual([true])
  })

  it('hands a piece over only after its screens arrive', async () => {
    let releaseScreens = () => {}
    const screens = new Promise<typeof SCREEN_TEXT>(resolve => (releaseScreens = () => resolve(SCREEN_TEXT)))
    let afterScreens: AfterScreens = load => load
    registerModules(routerWithHome(), [sampleModule(() => screens, (_, gate) => (afterScreens = gate))])
    let handedOver = false

    const piece = afterScreens(async () => 'piece')().then(loaded => {
      handedOver = true
      return loaded
    })
    await flushPromises()
    expect(handedOver).toBe(false)
    releaseScreens()

    await expect(piece).resolves.toBe('piece')
  })

  it('tries the screens again after a failed load', async () => {
    const loadScreens = vi.fn<NonNullable<PaidModule['loadScreens']>>()
    loadScreens.mockRejectedValueOnce(new Error('offline')).mockResolvedValue(SCREEN_TEXT)
    let afterScreens: AfterScreens = load => load
    registerModules(routerWithHome(), [sampleModule(loadScreens, (_, gate) => (afterScreens = gate))])

    await expect(afterScreens(async () => 'piece')()).rejects.toThrow('offline')
    await expect(afterScreens(async () => 'piece')()).resolves.toBe('piece')
    expect(loadScreens).toHaveBeenCalledTimes(2)
  })
})

describe('a module behind a capability', () => {
  it('gates every route it adds on that capability, keeping the route’s own meta', () => {
    const router = routerWithHome()
    const module: PaidModule = {
      messages: {},
      capability: 'monitoring',
      routes: [{ path: '/gated', component: { render: () => null }, meta: { allowedRoles: ['Operator'] } }],
      extend: () => {},
    }

    registerModules(router, [module])

    expect(router.resolve('/gated').meta).toEqual({ allowedRoles: ['Operator'], capability: 'monitoring' })
  })

  it('gates every route of each registered module that names a capability', () => {
    const router = routerWithHome()
    registerModules(router)

    for (const module of PAID_MODULES.filter(gated => gated.capability)) {
      for (const route of module.routes ?? []) {
        expect(router.getRoutes().find(added => added.path === route.path)?.meta.capability, route.path).toBe(module.capability)
      }
    }
  })
})
