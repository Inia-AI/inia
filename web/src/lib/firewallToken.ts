import { loadScript } from './loadScript'

/** The script AWS WAF serves for its bot and fraud rules. */
interface AwsWafIntegration {
  getToken(): Promise<string>
}

declare global {
  interface Window {
    AwsWafIntegration?: AwsWafIntegration
  }
}

/** The page names it where a deployment's web container is given the web ACL's integration URL. */
export const FIREWALL_META = 'aws-waf-integration-url'
export const FIREWALL_TOKEN_WAIT_MS = 2000

/**
 * Loads AWS WAF's challenge script where the page names an integration URL, and does nothing elsewhere.
 * Once loaded, the script fetches its token and notes how the sign-in form is used.
 */
export function loadFirewallScript(): Promise<void> {
  const url = document.querySelector<HTMLMetaElement>(`meta[name="${FIREWALL_META}"]`)?.content
  return url ? loadScript(`${url}/challenge.js`, () => !!window.AwsWafIntegration) : Promise.resolve()
}

/**
 * Waits for the token of AWS WAF's script, at most {@link FIREWALL_TOKEN_WAIT_MS}, so the firewall can
 * score the sign-up request that follows; requests to the page's own origin carry its cookie.
 */
export async function awaitFirewallToken(): Promise<void> {
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    await Promise.race([
      loadFirewallScript().then(() => window.AwsWafIntegration?.getToken()),
      new Promise<void>(resolve => { timer = setTimeout(resolve, FIREWALL_TOKEN_WAIT_MS) }),
    ])
  } catch {
    // The token is a signal for the firewall, never a gate: a script that fails leaves sign-in to go on without it.
  } finally {
    clearTimeout(timer)
  }
}
