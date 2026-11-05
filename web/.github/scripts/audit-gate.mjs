import { readFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'

export const IGNORED = new Map([
  [
    'GHSA-vfj7-8cjw-p6xm',
    'braces: stack-exhaustion denial of service from deeply nested patterns. No patched version exists, and braces reaches this repo only through @vue/eslint-config-typescript, a dev dependency (npm audit --omit=dev finds nothing). Remove this entry once braces ships a fix.',
  ],
])

const BLOCKING_SEVERITIES = new Set(['high', 'critical'])

const advisoryId = advisory => advisory.url.split('/').pop()

export function gate(report) {
  if (!report || typeof report.vulnerabilities !== 'object') {
    return {
      ok: false,
      blocking: [],
      ignored: [],
      error:
        report?.error?.summary ?? 'npm audit returned no vulnerability report',
    }
  }
  const advisories = new Map()
  for (const vulnerability of Object.values(report.vulnerabilities)) {
    for (const cause of vulnerability.via) {
      if (typeof cause === 'object') advisories.set(cause.url, cause)
    }
  }
  const severe = [...advisories.values()].filter(a =>
    BLOCKING_SEVERITIES.has(a.severity),
  )
  const blocking = severe.filter(a => !IGNORED.has(advisoryId(a)))
  return {
    ok: blocking.length === 0,
    blocking,
    ignored: severe.filter(a => IGNORED.has(advisoryId(a))),
  }
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const result = gate(JSON.parse(readFileSync(process.argv[2], 'utf8')))
  if (result.error) console.log(`npm audit failed: ${result.error}`)
  for (const a of result.blocking)
    console.log(
      `${a.severity.toUpperCase()} ${advisoryId(a)} ${a.name}: ${a.title} (${a.url})`,
    )
  for (const a of result.ignored)
    console.log(`ignored ${advisoryId(a)}: ${IGNORED.get(advisoryId(a))}`)
  process.exit(result.ok ? 0 : 1)
}
