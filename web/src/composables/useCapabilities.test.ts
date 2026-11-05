import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import API from '@/api'
import { CAPABILITIES } from '@/constants/capabilities'
import { answerService } from '@/test/helpers/answerService'
import { answerCapabilities, EVERY_CAPABILITY, resetCapabilities, without } from '@/test/helpers/capabilities'
import { useCapabilities } from './useCapabilities'

const Probe = defineComponent({
  setup() {
    const has = useCapabilities()
    return () => h('ul', EVERY_CAPABILITY.filter(capability => has(capability)).map(capability => h('li', capability)))
  },
})

const mountProbe = () => mount(Probe, { global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }]] } })
const shown = (wrapper: ReturnType<typeof mountProbe>) => wrapper.findAll('li').map(item => item.text())

describe('useCapabilities', () => {
  afterEach(resetCapabilities)

  it('gives every capability and reads nothing where no organization can lack one', async () => {
    const sent = answerService(API.Users.service.instance, () => ({ status: 200, data: { value: [] } }))

    const wrapper = mountProbe()
    await flushPromises()

    expect(shown(wrapper)).toEqual(EVERY_CAPABILITY)
    expect(sent).toEqual([])
  })

  it('hides what the active organization lacks once its list arrives, and shows nothing gated before', async () => {
    const sent = answerCapabilities(without(CAPABILITIES.RUN_LOGS, CAPABILITIES.BASELINES))

    const wrapper = mountProbe()
    expect(shown(wrapper)).toEqual([])
    await flushPromises()

    expect(shown(wrapper)).toEqual(without(CAPABILITIES.RUN_LOGS, CAPABILITIES.BASELINES))
    expect(sent.map(request => request.url)).toEqual(['api/v1/users/me/tenants'])
  })

  it('reads the list once for every piece on the screen', async () => {
    const sent = answerCapabilities(EVERY_CAPABILITY)
    const queryClient = new QueryClient()

    mount(defineComponent({ setup: () => () => [h(Probe), h(Probe), h(Probe)] }), { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
    await flushPromises()

    expect(sent).toHaveLength(1)
  })

  it('leaves every feature on when the read gives no list, since the API refuses what is missing either way', async () => {
    answerCapabilities(null)

    const wrapper = mountProbe()
    await flushPromises()

    expect(shown(wrapper)).toEqual(EVERY_CAPABILITY)
  })
})
