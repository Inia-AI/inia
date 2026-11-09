import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'

const { setupTotp, toast, qrCodeImageSource } = vi.hoisted(() => ({
  setupTotp: vi.fn(),
  toast: vi.fn(),
  qrCodeImageSource: vi.fn(),
}))

vi.mock('@/api', () => {
  const idleMutation = () => ({ mutateAsync: vi.fn(), isPending: ref(false) })
  return {
    default: {
      Authentication: {
        useTwoFactorStatus: () => ({ data: ref({ isEnabled: false }), isLoading: ref(false) }),
        useTrustedDevices: () => ({ data: ref([]), isLoading: ref(false) }),
        useSetupTotp: () => ({ mutateAsync: setupTotp, isPending: ref(false) }),
        useEnableTotp: idleMutation,
        useDisableTotp: idleMutation,
        useRegenerateRecoveryCodes: idleMutation,
        useRemoveTrustedDevice: idleMutation,
        useRemoveAllTrustedDevices: idleMutation,
      },
    },
  }
})
vi.mock('@tanstack/vue-query', () => ({ useQueryClient: () => ({ invalidateQueries: vi.fn() }) }))
vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast }) }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/components/ui/dialog', async () => (await import('@/test/helpers/uploaderMocks')).dialogModule)
vi.mock('@/lib/qrCode', () => ({ qrCodeImageSource }))

import TwoFactorSettings from './TwoFactorSettings.vue'

const startSetup = async () => {
  const wrapper = mount(TwoFactorSettings)
  await wrapper.findAll('button').find(button => button.text() === 'page.twoFactor.settings.enable')!.trigger('click')
  await flushPromises()
  return wrapper
}

describe('TwoFactorSettings setup', () => {
  beforeEach(() => {
    toast.mockReset()
    setupTotp.mockResolvedValue({ sharedKey: 'ABCD EFGH', authenticatorUri: 'otpauth://totp/Inia%20AI:jane?secret=ABCDEFGH' })
  })

  it('draws the code for the authenticator address beside the key to type', async () => {
    qrCodeImageSource.mockResolvedValue('data:image/svg+xml,qr')

    const wrapper = await startSetup()

    expect(qrCodeImageSource).toHaveBeenCalledWith('otpauth://totp/Inia%20AI:jane?secret=ABCDEFGH')
    expect(wrapper.find('.qr-code img').attributes('src')).toBe('data:image/svg+xml,qr')
    expect(wrapper.find('.key-display').text()).toBe('ABCD EFGH')
  })

  it('still opens the setup with the key to type when the code cannot be drawn', async () => {
    qrCodeImageSource.mockRejectedValue(new Error('chunk failed to load'))

    const wrapper = await startSetup()

    expect(wrapper.find('[data-stub="dialog"][data-open="true"]').exists()).toBe(true)
    expect(wrapper.find('.key-display').text()).toBe('ABCD EFGH')
    expect(wrapper.find('.qr-code').exists()).toBe(false)
    expect(toast).not.toHaveBeenCalled()
  })
})
