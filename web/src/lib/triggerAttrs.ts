const TRIGGER_ATTR = /^(id$|aria-|data-)/

// A form binding also hands a select its blur and change listeners, and the trigger blurs as soon
// as its list opens, so forwarding everything would validate an untouched field on open.
export function splitTriggerAttrs(attrs: Record<string, unknown>) {
  const trigger: Record<string, unknown> = {}
  const rest: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(attrs)) {
    if (TRIGGER_ATTR.test(key)) {
      trigger[key] = value
    } else {
      rest[key] = value
    }
  }
  return { trigger, rest }
}
