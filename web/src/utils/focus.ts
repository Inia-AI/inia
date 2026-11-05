const FOCUSABLE = 'button:not([disabled]), a[href], input:not([disabled]), [tabindex]:not([tabindex="-1"])'

export const focusFirstConnected = (...candidates: (HTMLElement | null | undefined)[]) => {
  const target = candidates.find(candidate => candidate?.isConnected)
  if (!target) {
    return
  }
  const control = target.matches(FOCUSABLE) ? target : target.querySelector<HTMLElement>(FOCUSABLE)
  ;(control ?? target).focus()
}
