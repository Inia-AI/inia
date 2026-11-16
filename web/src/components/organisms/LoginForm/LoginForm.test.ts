import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h, nextTick } from 'vue'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import API from '@/api'
import { answerService, SIGN_IN_TOKENS as TOKENS, type Answer } from '@/test/helpers/answerService'
import { extensionPoints, type SignInMethod } from '@/modules/extensionPoints'
import useThemeStore from '@/store/theme'

vi.mock('vue-i18n', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-i18n')>()),
  useI18n: () => ({ t: (key: string) => key }),
}))

import LoginForm from './LoginForm.vue'

const PROVIDERS_URL = 'api/v1/auth/providers'
const TOKEN_URL = 'api/v1/connect/token'

const answerAuthentication = (answers: Record<string, Answer>) =>
  answerService(API.Authentication.service.instance, ({ url }) => answers[url] ?? { status: 404 })

const providers = (...listed: object[]): Answer => ({ status: 200, data: { providers: listed, passwordReset: false } })

const mountForm = async (props: { busy?: boolean } = {}, at = '/login') => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/login', name: 'Login', component: { template: '<div />' } },
      { path: '/forgot-password', name: 'ForgotPassword', component: { template: '<div />' } },
    ],
  })
  await router.push(at)
  const pinia = createPinia()
  setActivePinia(pinia)
  const wrapper = mount(LoginForm, { props, global: { plugins: [router, pinia], mocks: { $t: (key: string) => key } }, attachTo: document.body })
  await flushPromises()
  return wrapper
}

const PASSWORD = { name: 'password', displayName: 'Email' }
const GOOGLE = { name: 'google', displayName: 'Google', clientId: 'client-id' }
const SAMPLE = { name: 'sample-method', displayName: 'Sample method' }

const SampleForm = defineComponent({
  props: { busy: Boolean },
  emits: ['signed-in'],
  setup: (props, { emit }) => () =>
    h('button', { 'data-testid': 'sample-form', 'data-busy': String(props.busy), onClick: () => emit('signed-in', TOKENS) }),
})


const sampleMethod: SignInMethod = {
  provider: SAMPLE.name,
  form: SampleForm,
  notice: defineComponent({ setup: () => () => h('p', { 'data-testid': 'sample-notice' }, 'Sample notice') }),
  titleKey: 'sample.title',
  subtitleKey: 'sample.subtitle',
  googleRefusalKeys: { sample_refusal: 'sample.refusal' },
}

afterEach(() => {
  document.body.innerHTML = ''
  extensionPoints.signInMethods.length = 0
})

describe('LoginForm', () => {
  it('offers Forgot password when the deployment can mail a reset link', async () => {
    answerAuthentication({ [PROVIDERS_URL]: { status: 200, data: { providers: [PASSWORD], passwordReset: true } } })

    const wrapper = await mountForm()

    const link = wrapper.find('a[href="/forgot-password"]')
    expect(link.exists()).toBe(true)
    expect(link.text()).toBe('page.login.form.forgotPassword')
  })

  it('hides Forgot password when no reset mail can go', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(PASSWORD) })

    const wrapper = await mountForm()

    expect(wrapper.text()).not.toContain('page.login.form.forgotPassword')
  })

  it('says the session ended when the app sent the person back for that reason', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(PASSWORD) })

    const wrapper = await mountForm({}, '/login?reason=session-ended')

    expect(wrapper.get('[data-testid="session-ended"]').text()).toBe('page.login.errors.sessionEnded')
  })

  it('says nothing about a session on a plain visit', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(PASSWORD) })

    const wrapper = await mountForm()

    expect(wrapper.find('[data-testid="session-ended"]').exists()).toBe(false)
  })

  it('says sign-in is unavailable when the provider list does not load', async () => {
    answerAuthentication({ [PROVIDERS_URL]: { status: 500 } })

    const wrapper = await mountForm()

    expect(wrapper.get('[data-testid="sign-in-unavailable"]').text()).toBe('page.login.errors.providersFailed')
    expect(wrapper.find('form').exists()).toBe(false)
  })
})

describe('LoginForm field errors', () => {
  const FIELD_ERRORS = /Invalid email|Password required/
  const fieldErrors = (wrapper: Awaited<ReturnType<typeof mountForm>>) => wrapper.findAll('.form-error').map(error => error.text())

  it('shows no field error when the form appears after the sign-in methods load', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(PASSWORD) })
    const instance = API.Authentication.service.instance
    const answerNow = instance.defaults.adapter as (config: InternalAxiosRequestConfig) => Promise<AxiosResponse>
    let release = () => {}
    const held = new Promise<void>(resolve => (release = resolve))
    instance.defaults.adapter = async config => {
      await held
      return answerNow(config)
    }
    const seen: string[] = []
    const observer = new MutationObserver(() => {
      seen.push(document.body.textContent ?? '')
    })
    observer.observe(document.body, { childList: true, subtree: true, characterData: true })

    const wrapper = await mountForm()
    release()
    await vi.waitFor(() => expect(wrapper.find('form.login-form').exists()).toBe(true))
    await flushPromises()
    observer.disconnect()

    expect(seen.filter(text => FIELD_ERRORS.test(text))).toEqual([])
    expect(wrapper.text()).not.toMatch(FIELD_ERRORS)
  })

  it('shows both field errors when the person submits empty fields', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(PASSWORD) })
    const wrapper = await mountForm()

    await wrapper.find('form.login-form').trigger('submit')

    await vi.waitFor(() => expect(fieldErrors(wrapper)).toEqual([expect.stringMatching(/Invalid email/), 'Password required']))
  })

  it('shows the email error when the person leaves the field with an invalid address', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(PASSWORD) })
    const wrapper = await mountForm()

    const email = wrapper.find('form.login-form input')
    await email.setValue('not-an-address')
    await email.trigger('blur')

    await vi.waitFor(() => expect(fieldErrors(wrapper)[0]).toMatch(/Invalid email/))
  })
})

describe('LoginForm with a module sign-in method', () => {
  beforeEach(() => {
    extensionPoints.signInMethods.push(sampleMethod)
  })

  it('looks as it does without the method while the list does not have it', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(PASSWORD, GOOGLE) })

    const wrapper = await mountForm()

    expect(wrapper.find('h1').text()).toBe('page.login.form.title')
    expect(wrapper.find('[data-testid="sample-form"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="sample-notice"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="sign-in-divider"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('page.login.form.orContinueWith')
  })

  it('leads with the method, then an or, then Google, with its heading and its notice last', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(SAMPLE, GOOGLE) })

    const wrapper = await mountForm()

    expect(wrapper.find('h1').text()).toBe('sample.title')
    expect(wrapper.find('.login-subtitle').text()).toBe('sample.subtitle')
    const html = wrapper.html()
    const positions = ['data-testid="sample-form"', 'data-testid="sign-in-divider"', 'id="google-signin-button"', 'data-testid="sample-notice"'].map(marker => html.indexOf(marker))
    expect(positions.every(position => position >= 0)).toBe(true)
    expect([...positions].sort((a, b) => a - b)).toEqual(positions)
  })

  it('shows no or when the method is the only way in', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(SAMPLE) })

    const wrapper = await mountForm()

    expect(wrapper.find('[data-testid="sign-in-divider"]').exists()).toBe(false)
  })

  it('hands on the method\'s tokens, and tells the method while the sign-in finishes', async () => {
    answerAuthentication({ [PROVIDERS_URL]: providers(SAMPLE) })
    const wrapper = await mountForm({ busy: true })

    await wrapper.get('[data-testid="sample-form"]').trigger('click')

    expect(wrapper.emitted('signed-in')).toEqual([[TOKENS]])
    expect(wrapper.get('[data-testid="sample-form"]').attributes('data-busy')).toBe('true')
  })
})

describe('LoginForm with Google', () => {
  let renderButton: ReturnType<typeof vi.fn>
  let googleCallback: (response: { credential: string }) => Promise<void>

  beforeEach(() => {
    localStorage.removeItem('theme')
    renderButton = vi.fn()
    window.google = {
      accounts: {
        id: {
          initialize: config => {
            googleCallback = config.callback as typeof googleCallback
          },
          renderButton,
          prompt: vi.fn(),
          disableAutoSelect: vi.fn(),
          cancel: vi.fn(),
        },
      },
    }
  })

  afterEach(() => {
    delete window.google
  })

  const signInWithGoogle = async (tokenAnswer: Answer) => {
    answerAuthentication({ [PROVIDERS_URL]: providers(GOOGLE, SAMPLE), [TOKEN_URL]: tokenAnswer })
    const wrapper = await mountForm()
    await googleCallback({ credential: 'google-credential' })
    await flushPromises()
    return wrapper
  }

  it.each([
    ['light', 'outline', 'filled_black'],
    ['dark', 'filled_black', 'outline'],
  ])('draws the button for the %s theme, and again when the theme changes', async (theme, buttonTheme, otherTheme) => {
    localStorage.setItem('theme', theme)
    answerAuthentication({ [PROVIDERS_URL]: providers(GOOGLE) })

    await mountForm()
    expect(renderButton).toHaveBeenLastCalledWith(expect.any(HTMLElement), expect.objectContaining({ theme: buttonTheme, text: 'continue_with' }))

    useThemeStore().toggleTheme()
    await nextTick()

    expect(renderButton).toHaveBeenCalledTimes(2)
    expect(renderButton).toHaveBeenLastCalledWith(expect.any(HTMLElement), expect.objectContaining({ theme: otherTheme }))
  })

  it('hands on the tokens of the listed method', async () => {
    extensionPoints.signInMethods.push(sampleMethod)

    const wrapper = await signInWithGoogle({ status: 200, data: TOKENS })

    expect(wrapper.emitted('signed-in')).toEqual([[TOKENS]])
  })

  it('shows the API\'s words when it refuses a Google account, and keeps the button', async () => {
    const wrapper = await signInWithGoogle({ status: 400, data: { error: 'sample_refusal', error_description: 'The provider refused this account.' } })

    expect(wrapper.get('[data-testid="google-error"]').text()).toBe('The provider refused this account.')
    expect(wrapper.find('#google-signin-button').isVisible()).toBe(true)
  })

  it('shows a listed method\'s words for the refusal when the API gave none', async () => {
    extensionPoints.signInMethods.push(sampleMethod)

    const wrapper = await signInWithGoogle({ status: 400, data: { error: 'sample_refusal' } })

    expect(wrapper.get('[data-testid="google-error"]').text()).toBe('sample.refusal')
  })

  it('says the network asked too often on a 429', async () => {
    const wrapper = await signInWithGoogle({ status: 429 })

    expect(wrapper.get('[data-testid="google-error"]').text()).toBe('page.login.errors.tooManyAttempts')
  })

  it('never shows the request library\'s own message', async () => {
    const wrapper = await signInWithGoogle({ status: 400, data: { error: 'invalid_grant' } })

    expect(wrapper.get('[data-testid="google-error"]').text()).toBe('page.login.errors.googleFailed')
  })
})
