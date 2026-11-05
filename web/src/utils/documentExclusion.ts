import { translated } from './translated'

const KEY_PREFIX = 'page.workflowDetails.documents.exclusionReasons'

/**
 * Words why a run did not use a document it was sent. A reason this client does not know reads
 * as a general "not used", because the code alone means nothing to an analyst.
 */
export function describeExclusionReason(
  t: (key: string) => string,
  reason: string | null | undefined,
): string {
  const code = reason?.trim()
  return (code ? translated(t, `${KEY_PREFIX}.${code}`) : null) ?? t(`${KEY_PREFIX}.other`)
}
