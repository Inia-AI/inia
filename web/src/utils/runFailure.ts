import { translated } from './translated'

/** What the reader can do about a failed run from the report page. */
export type RunFailureAction = 'changeDocuments' | 'runAgain'

export interface RunFailureDescription {
  /** The failure kind in plain words, or a general label for a kind this client does not know. */
  label: string
  /** What the reader needs: the kind's own sentence when it has one, else the detail the platform wrote. */
  body: string | null
  /** The platform's detail when `body` replaced it with the kind's sentence, so it can still be shown secondarily. */
  detail: string | null
  /** The one step that fixes the failure, or null when only the person who maintains the report type can. */
  action: RunFailureAction | null
}

const KEY_PREFIX = 'shared.runFailure'

/**
 * Kinds whose fix is not running the same report again. A refusal at intake needs other
 * documents; a report whose final checks are set up wrong fails the same way every time. Every
 * other kind, and a failure this client cannot classify, is worth one more run.
 */
const ACTION_BY_KIND: Record<string, RunFailureAction | null> = {
  RejectedAtIntake: 'changeDocuments',
  VerificationGroupShape: null,
  VerificationUnsatisfiable: null,
}

/**
 * Words a failed run for an analyst. Some kinds carry a detail written for the reader (the
 * missing document), which is shown as it is. Others carry infrastructure text (a model id, a
 * host lifetime); those kinds have a plain sentence in the locale that stands in for the detail,
 * and the detail stays available as secondary text. A kind this client does not know, or a
 * failure with no kind, reads as general advice, because its detail is whatever the platform
 * caught.
 */
export function describeRunFailure(
  t: (key: string) => string,
  failureKind: string | null | undefined,
  failureDetail: string | null | undefined,
): RunFailureDescription | null {
  const kind = failureKind?.trim() || null
  const detail = failureDetail?.trim() || null
  if (!kind && !detail) {
    return null
  }

  const label = kind ? translated(t, `${KEY_PREFIX}.kinds.${kind}.label`) : null
  const hint = label ? translated(t, `${KEY_PREFIX}.kinds.${kind}.hint`) : t(`${KEY_PREFIX}.fallbackHint`)

  return {
    label: label ?? t(`${KEY_PREFIX}.fallbackLabel`),
    body: hint ?? detail,
    detail: hint ? detail : null,
    action: kind && kind in ACTION_BY_KIND ? ACTION_BY_KIND[kind] : 'runAgain',
  }
}
