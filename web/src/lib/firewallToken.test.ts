import { afterEach, describe, expect, it, vi } from 'vitest'
import { awaitFirewallToken, FIREWALL_META, FIREWALL_TOKEN_WAIT_MS, loadFirewallScript } from './firewallToken'

const INTEGRATION_URL = 'https://abc123.edge.sdk.awswaf.com/abc123/def456'

const nameIntegrationUrl = () => {
  const meta = document.createElement('meta')
  meta.name = FIREWALL_META
  meta.content = INTEGRATION_URL
  document.head.appendChild(meta)
}

const challengeScripts = () => document.head.querySelectorAll(`script[src="${INTEGRATION_URL}/challenge.js"]`)

describe('the firewall token', () => {
  afterEach(() => {
    delete window.AwsWafIntegration
    document.head.innerHTML = ''
    vi.useRealTimers()
  })

  it('loads no script and waits for nothing on a page that names no integration URL', async () => {
    await expect(awaitFirewallToken()).resolves.toBeUndefined()

    expect(document.head.querySelectorAll('script')).toHaveLength(0)
  })

  it('loads AWS WAF’s challenge script once from the integration URL the page names', () => {
    nameIntegrationUrl()

    void loadFirewallScript()
    void loadFirewallScript()

    expect(challengeScripts()).toHaveLength(1)
  })

  it('waits until the script has its token', async () => {
    let release: (token: string) => void = () => {}
    const getToken = vi.fn(() => new Promise<string>(resolve => { release = resolve }))
    window.AwsWafIntegration = { getToken }
    nameIntegrationUrl()
    let done = false

    const waiting = awaitFirewallToken().then(() => { done = true })
    await vi.waitFor(() => expect(getToken).toHaveBeenCalledTimes(1))
    expect(done).toBe(false)
    release('waf-token')
    await waiting

    expect(done).toBe(true)
  })

  it(`goes on without the token after ${FIREWALL_TOKEN_WAIT_MS} ms`, async () => {
    vi.useFakeTimers()
    window.AwsWafIntegration = { getToken: vi.fn(() => new Promise<string>(() => {})) }
    nameIntegrationUrl()

    const waiting = awaitFirewallToken()
    await vi.advanceTimersByTimeAsync(FIREWALL_TOKEN_WAIT_MS)

    await expect(waiting).resolves.toBeUndefined()
  })

  it('goes on when the script fails', async () => {
    window.AwsWafIntegration = { getToken: vi.fn().mockRejectedValue(new Error('no token')) }
    nameIntegrationUrl()

    await expect(awaitFirewallToken()).resolves.toBeUndefined()
  })
})
